using System;

namespace FtpSync
{
    public class FtpConf
    {
        public string type { get; set; } = "ftp"; // ftp || sftp

        public string IP { get; set; }
        public string Login { get; set; }
        public string Passwd { get; set; }
        public int port { get; set; } = -1; // Auto

        public string FtpFolder { get; set; }
        public string LocalFolder { get; set; }

        public DateTime LastSyncGood { get; set; } = DateTime.Now;
        public int WaitToCloseApp { get; set; } = 5;
    }
}
