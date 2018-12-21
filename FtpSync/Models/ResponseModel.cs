using System;
using System.Collections.Generic;

namespace FtpSync.Models
{
    public class ResponseModel
    {
        /// <summary>
        /// Если false то один из файлов небыл загружен
        /// </summary>
        public bool syncGood;

        /// <summary>
        /// Текст ошибки
        /// </summary>
        public string errorMsg;
        
        public DateTime lastSyncGood;
        public List<UploadModel> uploads = new List<UploadModel>();
    }
}
