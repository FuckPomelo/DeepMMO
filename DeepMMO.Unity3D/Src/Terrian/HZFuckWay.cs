﻿using DeepCore.GameData.Zone;
using System;
using System.Collections.Generic;
using DeepCore.Game3D.Helper;
using DeepCore.Game3D.Slave;
using DeepCore.Game3D.Slave.Agent;
using DeepCore.Game3D.Slave.Layer;
using DeepCore.GameData.Helper;
using DeepCore.GameData.Zone.ZoneEditor;
using DeepCore.Vector;
using UnityEngine.AI;
using Vector2 = UnityEngine.Vector2;
using Vector3 = UnityEngine.Vector3;

namespace DeepMMO.Unity3D.Terrain
{


    /// <summary>
    /// 按照策划预先设置好的路线走路
    /// </summary>
    public class HZFuckWay : AbstractMoveAgent
    {
        private bool mFinish = false;
        public float EndDistance { get; set; }
        public object UserData { get; set; }

        public override bool IsEnd
        {
            get
            {
                return mNavPathPoints == null;
            }
        }

        public override bool IsDuplicate
        {
            get { return false; }
        }

        public override ILayerWayPoint WayPoints
        {
            get
            {
                if (way_points != null && way_points.Count > 0)
                {
                    return way_points[0];
                }
                return null;
            }
        }

        public override bool IsFinish
        {
            get { return mFinish; }
        }

        //private LayerEditorPoint start_point;
        private LayerEditorPoint end_point;
        private Predicate<LayerEditorPoint> select;
        //private ILayerWayPoint way_points;
        //todo 做多段寻路
        private List<ILayerWayPoint> way_points = new List<ILayerWayPoint>();
        private float cur_dir = 0;
        private bool auto_adjust;
        private Vector3 targetpos;
        private Vector3 cur_pos = Vector3.zero;
        private List<Vector3> mNavPathPoints;
        // 传送区域
        public List<RegionData> mRegionDatas;
        private bool b_wait;
        private bool b_hasTransport;

        public HZFuckWay(
            Vector3 targetpos,
            float endDistance = 0.05f,
            Predicate<LayerEditorPoint> select = null,
            bool autoAdjust = true,
            object ud = null)
        {
            this.auto_adjust = autoAdjust;
            this.EndDistance = endDistance;
            this.UserData = ud;
            this.targetpos = targetpos;
            this.select = select;
        }

      
        protected override void OnInit(LayerPlayer actor)
        {
            this.Owner.OnDoEvent += Owner_OnDoEvent;
            this.OnEnd += ActorMoveAgent_OnEnd;
            this.Start();
        }

        private void ActorMoveAgent_OnEnd(AbstractAgent agent)
        {
            if (this.Owner != null)
            {
                this.Owner.OnDoEvent -= Owner_OnDoEvent;
            }
        }

        protected override void OnDispose()
        {
            this.Owner.OnDoEvent -= Owner_OnDoEvent;
            base.OnDispose();
            mNavPathPoints = null;
        }


        private void Owner_OnDoEvent(LayerObject obj, ObjectEvent e)
        {
            //这里能不能给个reason
            if (e is UnitForceSyncPosEvent)
            {
                //this.Stop();
                if (b_hasTransport)
                {
                  b_wait = true;
                }
            }
        }


        private ILayerWayPoint GetWay_Points(DeepCore.Geometry.Vector3 beginpos, DeepCore.Geometry.Vector3 endpos)
        {
            ILayerWayPoint points = null;
            if (!auto_adjust) //直接寻路
            {
                points = Layer.Terrain3D.FindPath(beginpos, endpos);
            }
            else
            {


                bool hasFindPath = true;
                bool isStraight = false;
                WayPointAstar.FlagGraphPath wp_path = null;
             
//                if (!NavMesh.Raycast(srcUnitypos, targetpos, out NavMeshHit hit, 1)) //直接能到目标点就不走大路点
//                {
//                    this.way_points = Layer.Terrain3D.FindPath(Owner.Position, target_pos);
//                    hasFindPath = false;
//                    isStraight = true;
//                }

                //开始段
                if (hasFindPath && select != null)
                {
//                    if (start_point == null)
//                    {
//                        start_point = Layer.GetNearZoneFlag<LayerEditorPoint>(beginpos, select);
//                    }
                    var start_point =  Layer.GetNearZoneFlag<LayerEditorPoint>(beginpos, select);
                    if (start_point == null)
                    {
                        //Debug.Log("start_point == null");
                        hasFindPath = false;
                    }
                    else
                    {
                        
                        //结尾段
                        var end_point = Layer.GetNearZoneFlag<LayerEditorPoint>(endpos, select);
                        if (end_point == null)
                        {
                            //Debug.Log("end_point == null");
                            hasFindPath = false;
                        }
                        else if (start_point == end_point) //开始结尾一致
                        {
                            hasFindPath = false;
                        }
                        else
                        {
                            wp_path = Layer.FindPathWayPoint(start_point.Name, end_point.Name);
                            if (wp_path == null)
                            {
                                hasFindPath = false;
                            }

                            wp_path = OptimizePath(wp_path);
                        }
                    }
                }

                if (!hasFindPath )
                {
                    if (!isStraight)
                    {
                        points = Layer.Terrain3D.FindPath(beginpos, endpos);
                    }
                   
                }
                else
                {

                    var zonepos = new DeepCore.Geometry.Vector3(wp_path.Data.X, wp_path.Data.Y, wp_path.Data.Z);
                    var navwaypoint = Layer.Terrain3D.FindPath(beginpos, zonepos) as NavMeshWayPoint.NavMeshClientWayPoint;
                    if (navwaypoint != null)
                    {
                        if (Layer.Terrain3D is NavMeshWayPoint.NavMeshClientTerrain3D)
                        {
                            var pos = new List<DeepCore.Geometry.Vector3>();
                            zonepos = new DeepCore.Geometry.Vector3(wp_path.Data.X, wp_path.Data.Y, wp_path.Data.Z);
                            pos.Add(zonepos);
                            var curway = wp_path;
                            while (curway.Next != null)
                            {
                                var _postion = new DeepCore.Geometry.Vector3(curway.Next.Data.X, curway.Next.Data.Y, curway.Next.Data.Z);
                                pos.Add(_postion);
                                curway = curway.Next;
                            }

                            var isbreak = false;
                            for (int i = 0; i < pos.Count - 1; i++)
                            {
                                var navpoint = Layer.Terrain3D.FindPath(pos[i], pos[i + 1]) as NavMeshWayPoint.NavMeshClientWayPoint;
                                if (navpoint != null)
                                {
                                    navwaypoint.LinkNext(navpoint);
                                }
                                else
                                {
                                    isbreak = true;
                                    break;
                                }

                            }

                            if (isbreak)
                            {
                                navwaypoint = Layer.Terrain3D.FindPath(beginpos, end_point.Position) as NavMeshWayPoint.NavMeshClientWayPoint;
                            }

                        }

                        var end_points = Layer.Terrain3D.FindPath(end_point.Position, endpos) as NavMeshWayPoint.NavMeshClientWayPoint;
                        if (end_points != null)
                        {
                            navwaypoint.LinkNext(end_points);
                        }



                        points = navwaypoint;

                    }
                    else
                    {
                        points = Layer.Terrain3D.FindPath(beginpos, endpos);
                    }
                    
                }
            }

            return points;
        }

        private Tuple<float,ILayerWayPoint> Distance(DeepCore.Geometry.Vector3 beginpos,DeepCore.Geometry.Vector3 endpos)
        {
            var waypoint = GetWay_Points(beginpos,endpos);
            if (waypoint == null)
            {
                return new Tuple<float, ILayerWayPoint>(-1f,null);
            }
            return new Tuple<float,ILayerWayPoint>(waypoint.GetTotalDistance(),waypoint);
        }
        /// <summary>
        /// 再次开始
        /// </summary>
        public void Start()
        {

            var target_pos = BattleUtils.UnityPos2ZonePos(Owner.Parent.Terrain3D.TotalHeight, targetpos);
            var tarMaxdis = 99999f;
//            var begintransport = DeepCore.Geometry.Vector3.NaN;
//            var endtransport = DeepCore.Geometry.Vector3.NaN;
            ILayerWayPoint beginlayerwp = null;
            ILayerWayPoint endlaywp = null;
            //var stopwatch = Stopwatch.StartNew();
            if (mRegionDatas != null && mRegionDatas.Count > 0)//传送点判断
            {
                
                foreach (var regionData in mRegionDatas)
                {
                    var pos = new DeepCore.Geometry.Vector3(regionData.X, regionData.Y, regionData.Z);
                    foreach (var abilityData in regionData.Abilities)
                    {
                        if (abilityData is UnitTransportAbilityData tp)
                        {
                            if (string.IsNullOrEmpty(tp.NextPosition))
                            {
                                continue;
                            }
                            //stopwatch.Reset();
                            var pathbegin = Distance(Owner.Position, pos);
                            
                            //Debug.Log("pathbegin "+pos+" cost time" + stopwatch.ElapsedMilliseconds / 1000f);
                            if (pathbegin.Item1 == -1)
                            {
                                continue;
                            }
                            if (tp.AcceptForceForAll || 
                                (!tp.AcceptForceForAll 
                                 && tp.AcceptForce == Owner.Force))
                            {
                                var flag = Layer.GetFlag(tp.NextPosition);
                                
                                if (flag != null)
                                {
                                    //stopwatch.Reset();
                                    var pathend = Distance(flag.Position,target_pos);
                                    //Debug.Log("pathend "+ flag.Position +" cost time" + stopwatch.ElapsedMilliseconds / 1000f);
                                    
                                    if (pathend.Item1 == -1)
                                    {
                                        continue;
                                    }
                                    if (pathbegin.Item1 + pathend.Item1 < tarMaxdis)
                                    {
                                        tarMaxdis = pathend.Item1 + pathbegin.Item1;
//                                        begintransport = pos;
//                                        endtransport = flag.Position;
                                        beginlayerwp = pathbegin.Item2;
                                        endlaywp = pathend.Item2;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            
            //stopwatch.Reset();
            var distance = Distance(Owner.Position,target_pos);
            //Debug.Log("orgpathfind cost time" + stopwatch.ElapsedMilliseconds / 1000f);
            if (distance.Item1 != -1 && distance.Item1 < tarMaxdis)
            {
                way_points.Add(distance.Item2);
            }
            else if(beginlayerwp != null && endlaywp != null)
            {
                way_points.Add(beginlayerwp);
                way_points.Add(endlaywp);
                b_hasTransport = true;

//                var waysbegin = GetWay_Points(Owner.Position,begintransport);
//                var waysend = GetWay_Points(endtransport,target_pos);
//                if (waysbegin.Tail is NavMeshWayPoint.NavMeshClientWayPoint wt)
//                {
//                    wt.LinkNext(waysend as NavMeshWayPoint.NavMeshClientWayPoint);
//                }
//
//                way_points = waysbegin;
            }

            if (way_points != null && way_points.Count > 0)
            {
                mNavPathPoints = GetRoadPoint(way_points[0]);
//                Debug.Log("lastpath1="+mNavPathPoints[mNavPathPoints.Count - 1]);
//                Debug.Log("lastpath2="+mNavPathPoints[mNavPathPoints.Count - 2]);
            }
            
        }

        public List<Vector3> GetRoadPoint(ILayerWayPoint waypoint)
        {
            var points = new List<Vector3>();
            if (waypoint != null)
            {
                var postion = waypoint.Position.ConvertToUnityPos(Owner.Parent.Terrain3D.TotalHeight);
                points.Add(postion);
                var curway = waypoint;
                while (curway.Next != null)
                {
                    var _postion = curway.Next.Position.ConvertToUnityPos(Owner.Parent.Terrain3D.TotalHeight);
                    points.Add(_postion);
                    curway = curway.Next;
                }

//                distance = Vector3.Distance(targetpos, mNavPathPoints[mNavPathPoints.Count - 1]);
//                if (distance > 5) //最终点大于目标点 算寻路失败
//                {
//                    way_points = null;
//                }
            }

            else
            {
                return null;
            }
            return points;
        }
        //路线优化
        private WayPointAstar.FlagGraphPath OptimizePath(WayPointAstar.FlagGraphPath navwaypoint)
        {

            if (navwaypoint.Next == null)
            {
                return navwaypoint;
            }

            var curwaypoint = navwaypoint;
            //        var srcUnitypos = Owner.Position.ConvertToUnityPos(Owner.Parent.Terrain3D.TotalHeight);
            //        var curwayUnitypos = curwaypoint.Position.ConvertToUnityPos(Owner.Parent.Terrain3D.TotalHeight);
            //        float distance = UnityEngine.Vector3.Distance(srcUnitypos, curwayUnitypos);
            //        var nextpos = curwaypoint.Next.Position.ConvertToUnityPos(Owner.Parent.Terrain3D.TotalHeight);
            //        var startdistance =  UnityEngine.Vector3.Distance(curwayUnitypos, nextpos);
            //        if (!NavMesh.Raycast(srcUnitypos,nextpos,out NavMeshHit hit,1)) // 如果可以直达
            //        {
            //            var dis = UnityEngine.Vector3.Distance(srcUnitypos, nextpos); // 判断直达还是从路点1走比较近
            //            if (dis < distance + startdistance)
            //            {
            //                curwaypoint = curwaypoint.Next as NavMeshWayPoint.NavMeshClientWayPoint;
            //            }
            //             
            //        }
            var srcUnitypos = Owner.Position.ConvertToUnityPos(Owner.Parent.Terrain3D.TotalHeight);

            var forward = (targetpos - srcUnitypos).normalized;
            while (curwaypoint.Next != null) //把能直接走到的点都拿出来 然后找最近的那个
            {
                var pos = new DeepCore.Geometry.Vector3(curwaypoint.Data.X, curwaypoint.Data.Y, curwaypoint.Data.Z);
                var curwayUnitypos = pos.ConvertToUnityPos(Owner.Parent.Terrain3D.TotalHeight);

                if (!NavMesh.Raycast(srcUnitypos, curwayUnitypos, out NavMeshHit hit, 1))
                {
                    var dir = (curwayUnitypos - srcUnitypos).normalized;
                    var ret = Vector3.Dot(forward, dir);
                    if (ret >= 0)
                    {
                        return curwaypoint;
                    }
                }
                else
                {
                    break;
                }

                curwaypoint = curwaypoint.Next;
            }

            return curwaypoint;


        }

        /// <summary>
        /// 外部打断寻路.
        /// </summary>
        public void Stop()
        {
            if (way_points != null)
            {
               way_points.Clear();
            }
            
            mNavPathPoints = null;
        }


        public Vector2 Get3DTo2DZonePostion(Vector3 pos)
        {
            var nextposZonePos = BattleUtils.UnityPos2ZonePos(Owner.Parent.Terrain3D.TotalHeight, pos);
            return new Vector2(nextposZonePos.X, nextposZonePos.Y);
        }

        private bool checkTargetDistance()
        {
            float distance = Vector3.Distance(cur_pos, targetpos);

            if (distance <= EndDistance)
            {
                return true;
            }

            return false;
        }

        protected override void BeginUpdate(int intervalMS)
        {
            cur_dir = Owner.Direction;
            cur_pos = Owner.GetUnityPos();
            if ( !b_wait && (mNavPathPoints == null || mNavPathPoints.Count == 0))
            {
                if (b_hasTransport)
                {
                    Owner.SendUnitAxisAngle(0, 0, cur_dir);
                    return;
                }
                if (checkTargetDistance())
                {
                    mFinish = true;
                    Stop();
                }
            }
            else
            {
                if (b_wait)
                {
                    b_wait = false;
                    b_hasTransport = false;
                    if (way_points.Count > 1)
                    {
                        way_points.RemoveAt(0);
                        mNavPathPoints = GetRoadPoint(way_points[0]);
                    }
                }
                
                var nextpos = mNavPathPoints[0];
                var curpos2d = new Vector2(Owner.Position.X, Owner.Position.Y);
                var nextpos2d = Get3DTo2DZonePostion(nextpos); //nextpos.GetToZonePosSimpleNumberVector2(Owner.Parent.Terrain3D.TotalHeight);
                
                
                
                float length = MoveHelper.GetDistance(intervalMS, Owner.MoveSpeedSEC);


                float targetdistance = Vector3.Distance(cur_pos, targetpos);
                float nextdistance = Vector2.Distance(curpos2d, nextpos2d);

                if (checkTargetDistance())
                {
                    mFinish = true;
                    Stop();
                    return;
                }

                if (MathVector3D.moveTo(ref curpos2d, nextpos2d, length))
                {
                    if ((nextdistance <= length || Vector2.Distance(curpos2d, nextpos2d) <= 0.1f)
                        && mNavPathPoints != null && mNavPathPoints.Count > 0)
                    {
                        mNavPathPoints.RemoveAt(0);
                        if (mNavPathPoints.Count == 0)
                        {
                            if (!b_hasTransport)
                            {
                                mFinish = true;
                                Stop();
                            }
                            Owner.SendUnitAxisAngle(0, 0, cur_dir);
                            return;
                        }

                        nextpos = mNavPathPoints[0];
                        nextpos2d = Get3DTo2DZonePostion(nextpos);
                        MathVector3D.moveTo(ref curpos2d, nextpos2d, length - nextdistance);
                        //Debug.Log("curpos2d========"+curpos2d +" nextpos========"+nextpos);

                    }
                }

                //Debug.Log("cur_pos3========"+curpos2d );
                var pos = curpos2d;
                var ownerpos = Owner.Position;
                var dis = MathVector3D.Get2DDistance(pos.x, pos.y, ownerpos.X, ownerpos.Y);

                if (targetdistance <= 0.05f)
                {
                    mFinish = true;
                    Stop();
                }
                else
                {
                    //Debug.Log(" nextpos1=========="+pos +" dis======="+dis);

                    if (dis <= 0.05f) //精度修正
                    {
                        pos = nextpos2d;
                        //Debug.Log(" nextpos2=========="+pos);
                    }

                    cur_dir = MathVector.getDegree(ownerpos.X, ownerpos.Y, pos.x, pos.y);
                    Owner.SendUnitAxisAngle(cur_dir, length, cur_dir);
                }


            }

        }

    }

}