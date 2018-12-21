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
using System.Threading.Tasks;

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
            
            // Кодировка вывода
            Console.OutputEncoding = Encoding.UTF8;

            #region Нельзя запускать несколько exe одновременно
            bool createdNew;
            Mutex M = new Mutex(true, "FtpSync", out createdNew);
            if (!createdNew)
            {
                WriteLine(Methods.errorMsg, "FtpSync.exe уже запущен");
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
                        }
                        catch { }

                        if (!ftp.IsConnected)
                        {
                            WriteLine(Methods.errorMsg, "FTP connected is false");
                            WaitToCloseApp(conf);
                            return;
                        }

                        break;
                    }
                #endregion

                #region SFTP
                case "sftp":
                    {
                        try
                        {
                            sftp = new SftpClient(conf.IP, conf.port == -1 ? 22 : conf.port, conf.Login, conf.Passwd);
                            sftp.Connect();
                        }
                        catch { }

                        if (!sftp.IsConnected)
                        {
                            WriteLine(Methods.errorMsg, "SFTP connected is false");
                            WaitToCloseApp(conf);
                            return;
                        }

                        break;
                    }
                #endregion
            }
            #endregion
            
            // Получаем список папок
            List<string> directories = new List<string>() { BaseDir };
            directories.AddRange(Directory.GetDirectories(BaseDir, "*", SearchOption.AllDirectories));

            #region Копируем файлы на FTP/SFTP
            foreach (var folder in directories)
            {
                bool IsCreateDirectory = false;

                // Получаем все файлы в папке
                Parallel.ForEach(Directory.GetFiles(folder, "*", SearchOption.TopDirectoryOnly), new ParallelOptions { MaxDegreeOfParallelism = (conf.type == "ftp" ? 1 : 10) }, localFile =>
                {
                    // Файл не изменился
                    if (conf.LastSyncGood > new FileInfo(localFile).LastWriteTime)
                        return;

                    #region Создаем папку на FTP/SFTP
                    if (!IsCreateDirectory)
                    {
                        IsCreateDirectory = true;
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
                    }
                    #endregion

                    // Загуржаем файл на сервер
                    UploadFile(conf.type, localFile, localFile.Replace("\\", "/").Replace(BaseDir, conf.FtpFolder));
                });
            }
            #endregion

            #region Сохраняем время последней синхронизации
            if (SyncGood && fileConf != "base64")
            {
                conf.LastSyncGood = LastSyncGood;
                File.WriteAllText(fileConf, JsonConvert.SerializeObject(conf, Formatting.Indented));
            }
            #endregion

            // Выводим финальный ответ
            WriteLine(Methods.syncGood, SyncGood);
            WriteLine(Methods.lastSyncGood, SyncGood ? LastSyncGood : conf.LastSyncGood);

            // Таймаут
            WaitToCloseApp(conf);
        }


        #region UploadFile
        /// <summary>
        /// Передать локальный файл на удаленый сервер
        /// </summary>
        /// <param name="type">ftp/sftp</param>
        /// <param name="localFile">Полный путь к локальному файлу</param>
        /// <param name="remoteFile">Полный путь к удаленому файлу</param>
        static void UploadFile(string type, string localFile, string remoteFile)
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

            // Выводим результат
            WriteLine(Methods.uploadFile, md);
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

        #region WaitToCloseApp
        /// <summary>
        /// 
        /// </summary>
        /// <param name="conf"></param>
        static void WaitToCloseApp(FtpConf conf)
        {
            // Таймаут
            if (conf.WaitToCloseApp > 0)
                Thread.Sleep(1000 * conf.WaitToCloseApp);
        }
        #endregion

        #region WriteLine
        /// <summary>
        /// 
        /// </summary>
        /// <param name="method"></param>
        /// <param name="data"></param>
        static void WriteLine(Methods method, object data)
        {
            Console.WriteLine(JsonConvert.SerializeObject(new ResponseModel()
            {
                method = method.ToString(),
                data = data
            }));
        }
        #endregion
    }
}
