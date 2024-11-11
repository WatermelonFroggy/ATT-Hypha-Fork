using Alta.Api.DataTransferModels.Models.Responses;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hypha.Core
{
    public class ModdedServerInfo : GameServerInfo
    {
        public string IP { get; set; }
        public int Port { get; set; }
    }
}
