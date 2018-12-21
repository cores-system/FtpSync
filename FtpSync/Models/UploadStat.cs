namespace FtpSync.Models
{
    public class UploadStat
    {
        public UploadStat(int position, int length)
        {
            this.position = position;
            this.length = length;
        }

        public int position;
        public int length;
    }
}
