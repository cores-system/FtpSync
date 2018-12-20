using FluentFTP;
using Newtonsoft.Json;
using System;
using System.IO;
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
            ftp = new FtpClient(conf.IP, 21, conf.Login, conf.Passwd);

            Console.Write("Connect => ");
            ftp.Connect();
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
