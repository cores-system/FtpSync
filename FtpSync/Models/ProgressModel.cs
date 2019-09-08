namespace FtpSync.Models
{
    public class ProgressModel
    {
        public ProgressModel(string localFile, string remoteFile, double percent)
        {
            this.localFile = localFile;
            this.remoteFile = remoteFile;
            this.percent = percent;
        }

        public string localFile;
        public string remoteFile;
        public double percent;
    }
}
