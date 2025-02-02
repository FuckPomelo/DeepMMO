﻿using DeepMMO.Protocol.Client;
using DeepMMO.Server.Gate;
using System;
using System.Threading.Tasks;
using DeepMMO.Data;

namespace DeepMMO.Server
{
    public class ServerPassport
    {
        public virtual Task<ServerPassportData> VerifyAsync(ClientEnterGateRequest req)
        {
            return Task.FromResult(new ServerPassportData(false, 0));
        }
        public virtual Task<ServerPassportEnterGame> VerifyEnterGameAsync(ClientEnterServerRequest server, ClientEnterGameRequest req)
        {
            return Task.FromResult(new ServerPassportEnterGame(){ Verified  = true});
        }
        public virtual Task<string> FormatAccountAsync(ClientEnterGateRequest req)
        {
            throw new Exception("No Implement");
        }
    }
}
