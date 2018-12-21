using FluentFTP;
using FtpSync.Models;
using Newtonsoft.Json;
using Renci.SshNet;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Authentication;
using System.Text;
using System.Threading;

namespace FtpSync
{
    class Program
    {
        static FtpClient ftp;
        static SftpClient sftp;
        static string BaseDir;
        static bool SyncGood = true;

        static void Main(string[] args)
        {
            // Debug
            //args = new string[] { "base64", "" };
            
            #region Нельзя запускать несколько exe одновременно
            bool createdNew;
            Mutex M = new Mutex(true, "FtpSync", out createdNew);
            if (!createdNew)
            {
                JsonConvert.SerializeObject(new ResponseModel()
                {
                    errorMsg = "FtpSync.exe уже запущен"
                });
                return;
            }
            #endregion
            
            // Пользовательский файл настроек
            string fileConf = args.Length > 0 ? args[0] : "conf.json";

            // Загружаем конфиги
            FtpConf conf = JsonConvert.DeserializeObject<FtpConf>(fileConf == "base64" ? Base64Decode(args[1]) : File.ReadAllText(fileConf));
            BaseDir = conf.LocalFolder.Replace("\\", "/");
            DateTime LastSyncGood = DateTime.Now;

            #region Подключаемся к FTP/FTPS/SFTP
            switch (conf.type)
            {
                #region FTP/FTPS
                case "ftp":
                    {
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

                        if (!ftp.IsConnected)
                        {
                            JsonConvert.SerializeObject(new ResponseModel()
                            {
                                errorMsg = "FTP connected is false"
                            });
                            return;
                        }

                        break;
                    }
                #endregion

                #region SFTP
                case "sftp":
                    {
                        sftp = new SftpClient(conf.IP, conf.port == -1 ? 22 : conf.port, conf.Login, conf.Passwd);
                        sftp.Connect();

                        if (!sftp.IsConnected)
                        {
                            JsonConvert.SerializeObject(new ResponseModel()
                            {
                                errorMsg = "SFTP connected is false"
                            });
                            return;
                        }

                        break;
                    }
                #endregion
            }
            #endregion

            // Модель ответа
            ResponseModel res = new ResponseModel()
            {
                syncGood = true,
                lastSyncGood = conf.LastSyncGood
            };

            #region Копируем файлы на FTP/SFTP
            foreach (var folder in Directory.GetDirectories(BaseDir, "*", SearchOption.AllDirectories))
            {
                // В папке ничего не изменилось
                if (conf.LastSyncGood > new FileInfo(folder).LastWriteTime)
                    continue;

                #region Создаем папку на FTP/SFTP
                string ftpFolder = folder.Replace("\\", "/").Replace(BaseDir, conf.FtpFolder);

                switch (conf.type)
                {
                    case "ftp":
                        ftp.CreateDirectory(ftpFolder);
                        break;
                    case "sftp":
                        sftp.CreateDirectory(ftpFolder);
                        break;
                }
                #endregion

                // Получаем все файлы в папке
                foreach (var localFile in Directory.GetFiles(folder, "*", SearchOption.TopDirectoryOnly))
                {
                    // Файл не изменился
                    if (conf.LastSyncGood > new FileInfo(localFile).LastWriteTime)
                        continue;

                    // Загуржаем файл на сервер
                    UploadFile(conf.type, localFile, localFile.Replace("\\", "/").Replace(BaseDir, conf.FtpFolder), ref res.uploads);
                }
            }
            #endregion

            #region Сохраняем время последней синхронизации
            if (SyncGood)
            {
                res.lastSyncGood = LastSyncGood;
                res.syncGood = true;

                if (fileConf != "base64")
                {
                    conf.LastSyncGood = LastSyncGood;
                    File.WriteAllText(fileConf, JsonConvert.SerializeObject(conf, Formatting.Indented));
                }
            }
            #endregion

            // Выводим ответ в json
            JsonConvert.SerializeObject(res);

            // Таймаут
            if (conf.WaitToCloseApp > 0)
                Thread.Sleep(1000 * conf.WaitToCloseApp);
        }


        #region UploadFile
        /// <summary>
        /// Передать локальный файл на удаленый сервер
        /// </summary>
        /// <param name="type">ftp/sftp</param>
        /// <param name="localFile">Полный путь к локальному файлу</param>
        /// <param name="remoteFile">Полный путь к удаленому файлу</param>
        static void UploadFile(string type, string localFile, string remoteFile, ref List<UploadModel> uploads)
        {
            // Модель файла
            var md = new UploadModel() { remoteFile = remoteFile };

            try
            {
                using (var localFileStream = File.OpenRead(localFile))
                {
                    // Расположение локального файла
                    md.localFile = localFile.Replace("\\", "/").Replace(BaseDir, "");

                    #region Загружаем файл на FTP/SFTP
                    switch (type)
                    {
                        case "ftp":
                            ftp.Upload(localFileStream, remoteFile, FtpExists.Overwrite, true);
                            break;
                        case "sftp":
                            sftp.UploadFile(localFileStream, remoteFile, true);
                            break;
                    }
                    #endregion

                    // Успех
                    md.uploadResult = true;
                }
            }
            catch (Exception ex)
            {
                SyncGood = false;
                md.uploadResult = false;
                md.errorMsg = ex.Message;
            }

            // Обновляем колекцию
            uploads.Add(md);
        }
        #endregion

        #region Base64Decode
        /// <summary>
        /// 
        /// </summary>
        /// <param name="base64Encoded"></param>
        static string Base64Decode(string base64Encoded)
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(base64Encoded));
        }
        #endregion
    }
}
