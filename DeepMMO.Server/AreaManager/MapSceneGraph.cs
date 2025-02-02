﻿using DeepCore;
using DeepCore.Astar;
using DeepCore.GameData.Zone.ZoneEditor;
using DeepCore.Geometry;
using DeepCore.Log;
using System;
using System.Collections.Generic;

namespace DeepMMO.Server.AreaManager
{
    /// <summary>
    /// 跨场景寻路网格
    /// </summary>
    public class MapSceneGrapAstar : Astar<MapSceneGrapAstar.SceneGraphNode, MapSceneGrapAstar.SceneGraphPath>
    {
        private static Logger log = new LazyLogger(nameof(MapSceneGrapAstar));
        private SceneGraphMap terrain;
        public MapSceneGrapAstar(MapTemplateData[] nodes)
        {
            this.terrain = new SceneGraphMap(nodes);
            base.InitGraph(terrain);
        }
        public override SceneGraphPath GenWayPoint(SceneGraphNode node)
        {
            return new SceneGraphPath(node);
        }         
        /// <summary>
                     /// 找到最近的入口
                     /// </summary>
                     /// <param name="pos"></param>
                     /// <returns></returns>
        public SceneNextLink GetNearEntry(int mapID, Vector3 pos)
        {
            var node = terrain.GetNode(mapID);
            if (node != null)
            {
                return node.GetNearEntry(pos);
            }
            return null;
        }
        protected override void SetTempNode(IMapNode node, ITempMapNode temp)
        {
            (node as SceneGraphNode).TempNode = temp;
        }
        protected override ITempMapNode GetTempNode(IMapNode node)
        {
            return (node as SceneGraphNode).TempNode;
        }

        /// <summary>
        /// 跨场景寻路
        /// </summary>
        /// <param name="srcMapID"></param>
        /// <param name="dstMapID"></param>
        /// <param name="dstMapNearPos">目标场景最近点</param>
        /// <returns></returns>
        public ArrayList<SceneNextLink> FindPath(int srcMapID, int dstMapID, Vector3? dstMapNearPos)
        {
            var snode = terrain.GetNode(srcMapID);
            if (snode == null) return null;
            var dnode = terrain.GetNode(dstMapID);
            if (dnode == null) return null;
            SceneGraphPath path;
            lock (this)
            {
                path = base.FindPath(snode, dnode, null);
            }
            if (path != null)
            {
                var ret = new ArrayList<SceneNextLink>();
                foreach (SceneGraphPath wp in path)
                {
                    var next = wp.Next;
                    if (next != null)
                    {
                        var info = wp.Node.GetNextInfo(next.Node.MapID);
                        ret.Add(info);
                    }
                }
                if (ret.Count > 0 && dstMapNearPos.HasValue)
                {
                    var near = dnode.GetNearEntry(dstMapNearPos.Value);
                    if (near != ret[ret.Count - 1])
                    {
                        ret.Add(near);
                    }
                }
                return ret;
            }
            return null;
        }
        public class SceneGraphMap : IAstarGraph<SceneGraphNode>
        {
            private readonly HashMap<int, SceneGraphNode> nodes;
            public int TotalNodeCount { get { return nodes.Count; } }
            public SceneGraphMap(MapTemplateData[] nodes)
            {
                this.nodes = new HashMap<int, SceneGraphNode>(nodes.Length);
                foreach (var data in nodes)
                {
                    var node = new SceneGraphNode(data);
                    this.nodes.Add(node.MapID, node);
                }
                foreach (var node in this.nodes.Values)
                {
                    node.InitNexts(this);
                }
            }
            public void Dispose()
            {
                foreach (var node in this.nodes.Values)
                {
                    node.Dispose();
                }
                nodes.Clear();
            }
            public void ForEachNodes(Action<SceneGraphNode> action)
            {
                foreach (var node in this.nodes.Values)
                {
                    action(node);
                }
            }
            internal SceneGraphNode GetNode(int mapID)
            {
                return nodes.Get(mapID);
            }
        }
        public class SceneGraphNode : IMapNode
        {
            /// <summary>
            /// 下个场景连接点
            /// </summary>
            private SceneGraphNode[] nexts_array;
            private HashMap<int, SceneNextLink> nexts = new HashMap<int, SceneNextLink>(1);
            /// <summary>
            /// 当前场景所有入口
            /// </summary>
            private List<SceneNextLink> current_entries = new List<SceneNextLink>();
            public int MapID { get; private set; }
            public MapTemplateData Data { get; private set; }
            public override IMapNode[] Nexts { get { return nexts_array; } }
            public override int CloseAreaIndex { get { return 0; } protected set { } }
            public override object Tag { get; set; }
            internal ITempMapNode TempNode;
            public SceneGraphNode(MapTemplateData data)
            {
                this.Data = data;
                this.MapID = data.id;
            }
            public override void Dispose()
            {
                nexts.Clear();
            }
            public override bool TestCross(IMapNode other)
            {
                return nexts.ContainsKey((other as SceneGraphNode).MapID);
            }
            public override float GetFatherG(IMapNode father) { return 1; }
            public override float GetTargetH(IMapNode target ) { return 1; }
            internal void InitNexts(SceneGraphMap map)
            {
                nexts.Clear();
                current_entries.Clear();
                var list = new List<SceneGraphNode>(1);
                if (Data.connect != null)
                {
                    this.current_entries.AddRange(Data.connect);
                    foreach (var next in Data.connect)
                    {
                        var ss = RPGServerBattleManager.Instance.GetSceneAsCache(Data.zone_template_id);
                        if (ss != null && ss.Regions.TryFind(e => e.Name == next.from_flag_name, out var from_rg))
                        {
                            next.from_flag_pos = new Vector3(from_rg.X, from_rg.Y, from_rg.Z);
                        }
                        else
                        {
                            throw new Exception($"Currernt Link Data Error : MapID={MapID} : {next}");
                        }
                        var next_node = map.GetNode(next.to_map_id);
                        if (next_node != null)
                        {
                            if (!nexts.ContainsKey(next_node.MapID))
                            {
                                var ds = RPGServerBattleManager.Instance.GetSceneAsCache(next_node.Data.zone_template_id);
                                if (ds != null && ds.Regions.TryFind(e => e.Name == next.to_flag_name, out var next_rg))
                                {
                                    next.to_flag_pos = new Vector3(next_rg.X, next_rg.Y, next_rg.Z);
                                    nexts.Add(next_node.MapID, next);
                                }
                                else
                                {
                                    //throw new Exception($"Next Link Data Error : MapID={MapID} : {next}");
                                    log.Error($"Next Link Data Error : MapID={MapID} : {next}");
                                }
                            }
                            list.Add(next_node);
                        }
                        else
                        {
                            log.Error($"Next Link Data Error : MapID={MapID} : {next}");
                        }
                    }
                }
                this.nexts_array = list.ToArray();
            }
            internal SceneNextLink GetNextInfo(int mapID)
            {
                return nexts.Get(mapID);
            }
            /// <summary>
            /// 找到最近的入口
            /// </summary>
            /// <param name="pos"></param>
            /// <returns></returns>
            internal SceneNextLink GetNearEntry(Vector3 pos)
            {
                SceneNextLink ret = null;
                var min = float.MaxValue;
                foreach (var entry in current_entries)
                {
                    var d = Vector3.DistanceSquared(entry.from_flag_pos, pos);
                    if (d < min)
                    {
                        ret = entry;
                        min = d;
                    }
                }
                return ret;
            }
        }
        public class SceneGraphPath : IWayPoint<SceneGraphNode, SceneGraphPath>
        {
            public MapTemplateData Data { get; private set; }
            public SceneGraphPath(SceneGraphNode map_node) : base(map_node)
            {
                this.Data = base.Node.Data;
            }
            public override bool PosEquals(SceneGraphPath w)
            {
                return Data.id == w.Data.id;
            }
        }

    }


}
