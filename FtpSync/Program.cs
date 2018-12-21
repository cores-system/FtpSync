using FluentFTP;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Security.Authentication;
using System.Threading;

namespace FtpSync
{
    class Program
    {
        static FtpClient ftp;
        static string BaseDir;
        static bool SyncGood = true;

        static void Main(string[] args)
        {
            // Загружаем конфиги
            FtpConf conf = JsonConvert.DeserializeObject<FtpConf>(File.ReadAllText("conf.json"));
            BaseDir = conf.LocalFolder.Replace("\\", "/");
            DateTime LastSyncGood = DateTime.Now;

            #region Подключаемся к FTP
            Console.Write("Connect => ");
            try
            {
                // create an FTP client
                ftp = new FtpClient(conf.IP, conf.port == -1 ? 21 : conf.port, conf.Login, conf.Passwd);

                // begin connecting to the server
                ftp.Connect();
            }
            catch
            {
                // FTPS
                ftp = new FtpClient(conf.IP, conf.port == -1 ? 21 : conf.port, conf.Login, conf.Passwd);
                ftp.EncryptionMode = FtpEncryptionMode.Explicit;
                ftp.SslProtocols = SslProtocols.Tls;
                ftp.ValidateCertificate += new FtpSslValidation(OnValidateCertificate);
                ftp.Connect();

                void OnValidateCertificate(FtpClient control, FtpSslValidationEventArgs e)
                {
                    // add logic to test if certificate is valid here
                    e.Accept = true;
                }
            }
            Console.WriteLine(ftp.IsConnected);

            if (!ftp.IsConnected)
            {
                Console.ReadLine();
                return;
            }
            #endregion

            #region Копируем файлы на FTP
            foreach (var folder in Directory.GetDirectories(BaseDir, "*", SearchOption.AllDirectories))
            {
                // В папке ничего не изменилось
                if (conf.LastSyncGood > new FileInfo(folder).LastWriteTime)
                    continue;

                // Создаем папку на FTP
                ftp.CreateDirectory(folder.Replace("\\", "/").Replace(BaseDir, conf.FtpFolder));

                // Получаем все файлы в папке
                foreach (var localFile in Directory.GetFiles(folder, "*", SearchOption.TopDirectoryOnly))
                {
                    // Файл не изменился
                    if (conf.LastSyncGood > new FileInfo(localFile).LastWriteTime)
                        continue;

                    // Загуржаем файл на сервер
                    UploadFile(localFile, localFile.Replace("\\", "/").Replace(BaseDir, conf.FtpFolder));
                }
            }
            #endregion

            #region Сохраняем время последней синхронизации
            if (SyncGood)
            {
                conf.LastSyncGood = LastSyncGood;
                File.WriteAllText("conf.json", JsonConvert.SerializeObject(conf, Formatting.Indented));
            }
            #endregion

            Console.WriteLine("\n\nSync Good");
            Thread.Sleep(1000 * conf.WaitToCloseApp);
            //Console.ReadLine();
        }


        #region UploadFile
        /// <summary>
        /// Передать локальный файл на удаленый сервер
        /// </summary>
        /// <param name="LocalFile">Полный путь к локальному файлу</param>
        /// <param name="RemoteFile">Полный путь к удаленому файлу</param>
        static void UploadFile(string LocalFile, string RemoteFile)
        {
            try
            {
                using (var LocalFileStream = File.OpenRead(LocalFile))
                {
                    Console.Write(LocalFile.Replace("\\", "/").Replace(BaseDir, "") + " => ");
                    ftp.Upload(LocalFileStream, RemoteFile, FtpExists.Overwrite, true);
                    Console.WriteLine(true);
                }
            }
            catch (Exception ex)
            {
                SyncGood = false;
                Console.WriteLine(ex.ToString());
            }
        }
        #endregion
    }
}
