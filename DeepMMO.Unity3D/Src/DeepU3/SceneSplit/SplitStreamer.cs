// #define CHECK_HASH_HITTING

using System;
using System.Collections.Generic;
using Cysharp.Text;
using DeepU3.Asset;
using DeepU3.Async;
using DeepU3.Timers;
using UnityEngine;
using UnityEngine.Profiling;

namespace DeepU3.SceneSplit
{
    public class RuntimeSceneSplit
    {
        public int[] pos;

        /// <summary>
        /// The scene Game object.
        /// </summary>
        public GameObject sceneGo;

        /// <summary>
        /// Was scene loaded.
        /// </summary>
        public bool loaded;

        public string path;

        /// <summary>
        /// 表示不从prefab读取,直接创建GameObject
        /// </summary>
        public SplitInfo splitInfo { get; set; }

        public SplitManager splitManager;
    }

    internal class ValidPosition
    {
        public int Min = int.MaxValue;
        public int Max = int.MinValue;
        private const int ArrayMaxSize = 1024;
        public readonly byte[] PositiveArray = new byte[ArrayMaxSize];
        public readonly byte[] NegativeArray = new byte[ArrayMaxSize];

        public bool IsValid(int index)
        {
            if (index >= 0)
            {
                return index < ArrayMaxSize && PositiveArray[index] > 0;
            }

            return -index < ArrayMaxSize && NegativeArray[-index] > 0;
        }
    }

    /// <summary>
    /// Streams async scene tiles
    /// </summary>
    public partial class SplitStreamer : MonoBehaviour
    {
        /// <summary>
        /// 简单块(没有生成prefab)
        /// </summary>
        public SplitInfo[] splits;

        public SplitterLayerSetting layerSetting;

        public LayerSettingTemplates template;

        /// <summary>
        /// The x position.
        /// </summary>
        public int xPos = int.MinValue;

        /// <summary>
        /// The y position.
        /// </summary>
        public int yPos = int.MinValue;

        /// <summary>
        /// The z position.
        /// </summary>
        public int zPos = int.MinValue;

        public int SqrMagnitude { get; private set; }
        private Vector3Int mDeloadingRange;


#if CHECK_HASH_HITTING
        private readonly Dictionary<int, HashSet<Vector3Int>> mHashHitCheck = new Dictionary<int, HashSet<Vector3Int>>();
#endif
        /// <summary>
        /// The scenes array.
        /// </summary>
        private readonly Dictionary<int, List<RuntimeSceneSplit>> mScenesArray = new Dictionary<int, List<RuntimeSceneSplit>>();


        /// <summary>
        /// The loaded scenes.
        /// </summary>
        [HideInInspector]
        public List<RuntimeSceneSplit> loadedScenes = new List<RuntimeSceneSplit>();

        private readonly Dictionary<string, GameObject> mSceneObjects = new Dictionary<string, GameObject>();


        private readonly HashSet<RuntimeSceneSplit> mLoadingSplits = new HashSet<RuntimeSceneSplit>();

        private readonly List<RuntimeSceneSplit> mScenesToDestroy = new List<RuntimeSceneSplit>();


        private readonly ValidPosition mValidX = new ValidPosition();
        private readonly ValidPosition mValidY = new ValidPosition();
        private readonly ValidPosition mValidZ = new ValidPosition();
        private int mXRange;
        private int mYRange;
        private int mZRange;
        private int mCheckArrayLength;
        private FrameScheduler mUnloadScheduler;

        private bool CheckPositionTiles(in Vector3 pos)
        {
            if (splits == null || splits.Length == 0)
            {
                return false;
            }

            var xPosCurrent = (layerSetting.splitSize.x != 0) ? Mathf.FloorToInt(pos.x / layerSetting.splitSize.x) : 0;
            var yPosCurrent = (layerSetting.splitSize.y != 0) ? Mathf.FloorToInt(pos.y / layerSetting.splitSize.y) : 0;
            var zPosCurrent = (layerSetting.splitSize.z != 0) ? Mathf.FloorToInt(pos.z / layerSetting.splitSize.z) : 0;
            if (xPosCurrent == xPos && yPosCurrent == yPos && zPosCurrent == zPos)
            {
                return false;
            }

            xPos = xPosCurrent;
            yPos = yPosCurrent;
            zPos = zPosCurrent;

            return true;
        }


        internal void LoadMiniSplit(SplitManager splitManager, MiniSplitInfo part, Action<ResultAsyncOperation<GameObject>> callback)
        {
            var instanceParam = new InstantiationParameters(part.position, part.rotation, splitManager.transform, false);
            var path = AssetManager.PathConverter.AssetPathToAddress(part.assetPath);
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogError($"[{splitManager.name}] not find {part.name} at {part.assetPath}");
                callback(ResultAsyncOperation<GameObject>.FromResult(null));
                return;
            }

            var assetAddress = InstantiationAssetAddress.String2Address(path, instanceParam);
            SplitStreamerPriorityLoader.Instance.AddQueue(assetAddress, this, part, splitManager.splitData.posID, callback);
        }

        private void LoadPart(RuntimeSceneSplit runtimeSceneSplit)
        {
            var splitAssetAddress = runtimeSceneSplit.splitInfo.assetPath;
            if (string.IsNullOrEmpty(splitAssetAddress))
            {
                if (runtimeSceneSplit.splitManager)
                {
                    OnSplitLoaded(runtimeSceneSplit);
                }
                else
                {
                    var o = new GameObject(runtimeSceneSplit.path);
                    OnSplitLoaded(o, runtimeSceneSplit);
                }
            }
            else
            {
                var path = AssetManager.PathConverter.AssetPathToAddress(splitAssetAddress);
                if (string.IsNullOrEmpty(path))
                {
                    Debug.LogError($"[{runtimeSceneSplit.path}] not find {splitAssetAddress} ");
                    OnSplitLoaded(null, runtimeSceneSplit);
                    return;
                }

                var instanceParam = new InstantiationParameters(transform, false);
                var assetAddress = InstantiationAssetAddress.String2Address(path, instanceParam);
                SplitStreamerPriorityLoader.Instance.AddQueue(assetAddress, this, runtimeSceneSplit, runtimeSceneSplit.pos, OnSplitLoaded);
            }
        }

        /// <summary>
        /// Start this instance, prepares scene collection into scene array, starts player position checker
        /// </summary>
        void Start()
        {
            mDeloadingRange = layerSetting.loadingRange + template.deloadingOffset;
            PrepareScenesArray();
        }

        private static int Mod(int x, int m)
        {
            return (x % m + m) % m;
        }


        #region loading and unloading

        private void TryLoading(int x, int y, int z, ICollection<RuntimeSceneSplit> splitsToLoad)
        {
            if (!mValidX.IsValid(x) || !mValidY.IsValid(y) || !mValidZ.IsValid(z))
            {
                return;
            }

            var hashCode = SplitUtils.GetSplitHashCode(x, y, z);
            if (!mScenesArray.TryGetValue(hashCode, out var splitList))
            {
                return;
            }

            //哈希碰撞
#if CHECK_HASH_HITTING
            if (mHashHitCheck.TryGetValue(hashCode, out var v3Set) && !v3Set.Contains(new Vector3Int(x, y, z)))
            {
                Debug.LogError($"hash hitting ({x},{y},{z}) <==> {string.Join(";", v3Set)}");
                return;
            }
#endif

            for (var i = 0; i < splitList.Count; i++)
            {
                var split = splitList[i];
                if (split.loaded)
                {
                    continue;
                }

                split.loaded = true;
                loadedScenes.Add(split);
                splitsToLoad.Add(split);
            }
        }


        private static readonly List<RuntimeSceneSplit> sScenesToLoadBuffer = new List<RuntimeSceneSplit>();


        /// <summary>
        /// Loads tiles in range
        /// </summary>
        private bool SceneLoading()
        {
            var xOut = xPos < mValidX.Min - mXRange || xPos > mValidX.Max + mXRange;
            var yOut = mYRange != 0 && (yPos < mValidY.Min - mYRange || yPos > mValidY.Max + mYRange);
            var zOut = zPos < mValidZ.Min - mZRange || zPos > mValidZ.Max + mZRange;
            if (xOut || yOut || zOut)
            {
                return false;
            }

            sScenesToLoadBuffer.Clear();
            for (var x = -mXRange + xPos; x <= mXRange + xPos; x++)
            {
                for (var y = -mYRange + yPos; y <= mYRange + yPos; y++)
                {
                    for (var z = -mZRange + zPos; z <= mZRange + zPos; z++)
                    {
                        TryLoading(x, y, z, sScenesToLoadBuffer);
                    }
                }
            }

            for (var i = sScenesToLoadBuffer.Count - 1; i >= 0; i--)
            {
                var split = sScenesToLoadBuffer[i];
                mLoadingSplits.Add(split);
                LoadPart(split);
            }

            return sScenesToLoadBuffer.Count > 0;
        }

        private void OnSplitLoaded(ResultAsyncOperation<GameObject> op)
        {
            var userData = op.UserData;
            var obj = op.Result;
            var split = (RuntimeSceneSplit) userData;
            OnSplitLoaded(obj, split);
        }

        private void OnSplitLoaded(RuntimeSceneSplit split)
        {
            mLoadingSplits.Remove(split);
            var splitManager = split.splitManager;
            splitManager.Init(this, split.splitInfo, split.path);
            mSceneObjects[split.path] = splitManager.gameObject;
        }

        private void OnSplitLoaded(GameObject obj, RuntimeSceneSplit split)
        {
            mLoadingSplits.Remove(split);
            if (!obj)
            {
                return;
            }

            obj.name = split.path;
            var splitManager = obj.GetOrAddComponent<SplitManager>();
            if (this)
            {
                splitManager.Init(this, split.splitInfo, split.path);
                mSceneObjects[split.path] = obj;
                obj.transform.SetParent(transform, false);
                obj.transform.localPosition = Vector3.zero;
                split.splitManager = splitManager;
                split.sceneGo = obj;
            }
            else
            {
                splitManager.Release();
            }
        }


        private bool IsEnterDeloadingRange(int x, int y, int z)
        {
            if (xPos == int.MinValue || yPos == int.MinValue || zPos == int.MinValue)
            {
                return true;
            }
            return Mathf.Abs(x - xPos) > mDeloadingRange.x
                   || Mathf.Abs(y - yPos) > mDeloadingRange.y
                   || Mathf.Abs(z - zPos) > mDeloadingRange.z;
        }

        private void UnLoadSplit(int index)
        {
            if (index >= mScenesToDestroy.Count)
            {
                return;
            }

            var item = mScenesToDestroy[index];
            if (!item.loaded)
            {
                return;
            }

            loadedScenes.Remove(item);
            if (mSceneObjects.TryGetValue(item.path, out var obj))
            {
                mSceneObjects.Remove(item.path);
            }

            var splitManager = item.splitManager;
            if (splitManager.isPrefab)
            {
                splitManager.Release();
                item.splitManager = null;
                item.sceneGo = null;
            }
            else
            {
                splitManager.ReleaseChildren();
            }

            item.loaded = false;
        }

        /// <summary>
        /// Unloads tiles out of range
        /// </summary>
        private void SceneUnloading()
        {
            Profiler.BeginSample("SceneUnloading");
            mUnloadScheduler.Complete();
            mScenesToDestroy.Clear();
            foreach (var item in loadedScenes)
            {
                if (item.sceneGo == null)
                {
                    continue;
                }

                var needToDestroy = true;
                for (var i = 0; i < item.pos.Length; i += 3)
                {
                    needToDestroy = IsEnterDeloadingRange(item.pos[i], item.pos[i + 1], item.pos[i + 2]);
                    if (!needToDestroy)
                    {
                        break;
                    }
                }

                if (needToDestroy)
                {
                    mScenesToDestroy.Add(item);
                }
            }

            mUnloadScheduler.Schedule(mScenesToDestroy.Count);

            Profiler.EndSample();
        }


        /// <summary>
        /// Unloads all tiles of streamer
        /// </summary>
        public void UnloadAllScenes()
        {
            foreach (var item in mScenesArray)
            {
                foreach (var split in item.Value)
                {
                    if (split.sceneGo != null)
                    {
                        if (mSceneObjects.TryGetValue(split.path, out var obj))
                        {
                            split.splitManager.Release();
                            split.splitManager = null;
                            mSceneObjects.Remove(split.path);
                        }
                    }

                    split.loaded = false;
                    split.sceneGo = null;
                }
            }

            loadedScenes.Clear();

            // Streamer.UnloadAssets(this);
        }

        #endregion

        #region prepare scene

        private static void AddValidPositionIndex(ValidPosition vp, int index)
        {
            vp.Min = Math.Min(index, vp.Min);
            vp.Max = Math.Max(index, vp.Max);
            if (index >= 0 && index < vp.PositiveArray.Length)
            {
                vp.PositiveArray[index] = 1;
            }
            else if (index < 0 && -index < vp.NegativeArray.Length)
            {
                vp.NegativeArray[-index] = 1;
            }
            else
            {
                Debug.LogError($"index out of range {index}");
            }
        }

        /// <summary>
        /// Prepares the scenes array from collection
        /// </summary>
        private void PrepareScenesArray()
        {
            SqrMagnitude = layerSetting.splitSize.sqrMagnitude;

            if (splits == null)
            {
                return;
            }

            mUnloadScheduler = this.AttachFrameScheduler(3f, UnLoadSplit);
            mXRange = layerSetting.loadingRange.x;
            mYRange = layerSetting.splitSize.y > 0 ? layerSetting.loadingRange.y : 0;
            mZRange = layerSetting.loadingRange.z;
            mCheckArrayLength = (mXRange * 2 + 1) * (mYRange * 2 + 1) * (mZRange * 2 + 1);

            for (var index = 0; index < splits.Length; index++)
            {
                var splitInfo = splits[index];
                var posId = splitInfo.posID;

                var sceneSplit = new RuntimeSceneSplit
                {
                    pos = posId,
                    splitInfo = splitInfo,
                    path = ZString.Format("{0}_{1}_{2}_{3}", name, posId[0], posId[1], posId[2])
                };

                for (var i = 0; i < posId.Length; i += 3)
                {
                    var x = posId[0];
                    var y = posId[1];
                    var z = posId[2];
                    var hashCode = SplitUtils.GetSplitHashCode(x, y, z);
                    if (!mScenesArray.TryGetValue(hashCode, out var splitList))
                    {
                        splitList = new List<RuntimeSceneSplit>();
                        mScenesArray.Add(hashCode, splitList);
                    }

                    AddValidPositionIndex(mValidX, x);
                    AddValidPositionIndex(mValidY, y);
                    AddValidPositionIndex(mValidZ, z);
#if CHECK_HASH_HITTING
                    if (!mHashHitCheck.TryGetValue(hashCode, out var hashList))
                    {
                        hashList = new HashSet<Vector3Int>();
                        mHashHitCheck.Add(hashCode, hashList);
                    }

                    hashList.Add(new Vector3Int(x, y, z));
#endif

                    splitList.Add(sceneSplit);
                }
            }
        }

        #endregion


        void OnDrawGizmosSelected()
        {
            if (layerSetting == null)
            {
                return;
            }

            // Display the explosion radius when selected
            Gizmos.color = layerSetting.color;
            Vector3 size = layerSetting.splitSize;
            if (Math.Abs(size.y) < 0.0001)
            {
                size.y = 10;
            }

            var posRoot = gameObject.scene.GetRootGameObjects()[0].transform.position;
            for (var x = -layerSetting.loadingRange.x + xPos; x <= layerSetting.loadingRange.x + xPos; x++)
            {
                for (var y = -layerSetting.loadingRange.y + yPos; y <= layerSetting.loadingRange.y + yPos; y++)
                {
                    for (var z = -layerSetting.loadingRange.z + zPos; z <= layerSetting.loadingRange.z + zPos; z++)
                    {
                        Gizmos.DrawWireCube(posRoot + new Vector3(x * size.x, y * size.y, z * size.z) + size * 0.5f, size);
                    }
                }
            }

            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(posRoot + new Vector3(xPos * size.x, yPos * size.y, zPos * size.z) + size * 0.5f, size);
        }
    }
}