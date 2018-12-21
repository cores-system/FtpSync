namespace FtpSync.Models
{
    public class UploadModel
    {
        public string localFile;
        public string remoteFile;

        public bool uploadResult;
        public string errorMsg;
    }
}
