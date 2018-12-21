using System;
using System.Collections.Generic;

namespace FtpSync.Models
{
    public class ResponseModel
    {
        public bool syncGood;
        public DateTime lastSyncGood;
        public string errorMsg;

        public List<UploadModel> uploads = new List<UploadModel>();
    }
}
