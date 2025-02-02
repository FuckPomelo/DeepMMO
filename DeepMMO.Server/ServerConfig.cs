﻿using DeepCore.Log;
using DeepCrystal.RPC;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DeepMMO.Server
{
    [LoadFromGlobalConfig]
    public sealed class TimerConfig
    {
        /// <summary>
        /// Connect同步状态到Gate
        /// </summary>
        public static int timer_sec_SyncConnectToGateNotify = 3;
        /// <summary>
        /// Gate处理等待队列
        /// </summary>
        public static int timer_sec_GateUpdateQueue = 10;

        /// <summary>
        /// Area同步状态到AreaManager
        /// </summary>
        public static int timer_sec_AreaStateNotify = 10;

        /// <summary>
        /// 角色刷新定时器.
        /// </summary>
        public static int timer_sec_OnPollingRoleModule = 3;

        /// <summary>
        /// Snap数据定时保存
        /// </summary>
        public static int timer_sec_SnapUpdateSeconds = 30;

        /// <summary>
        /// 玩家断线后，Session保持时间
        /// </summary>
        public static int timer_sec_SessionKeepTimeout = 15;

        /// <summary>
        /// 场景单位公共数据刷新.
        /// </summary>
        public static int timer_sec_OnPollingAreaZoneNode = 5;

        /// <summary>
        /// 副本无人后多久清理副本
        /// </summary>
        public static int timer_sec_ZoneKeepPlayerTimeout = 10;

        /// <summary>
        /// 定期储存数据10分钟.
        /// </summary>
        public static int timer_minute_SaveDataTimer = 5;

        /// <summary>
        /// GameOver后场景销毁延迟时间.
        /// </summary>
        public static int timer_sec_DelayDestoryTime = 30;

        /// <summary>
        ///AreaManager EventManager
        /// </summary>
        public static int timer_sec_EventUpdateTime = 1;

       
    }
}
