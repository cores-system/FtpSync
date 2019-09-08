using FluentFTP;
using FtpSync.Models;
using Newtonsoft.Json;
using Renci.SshNet;
using Renci.SshNet.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Authentication;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace FtpSync
{
    class Program
    {
        #region Program
        static FtpClient ftp;
        static SftpClient sftp;
        static string BaseDir;
        static bool SyncGood = true;
        static IDictionary<string, int> CreateDirectorys = new Dictionary<string, int>();
        #endregion

        static void Main(string[] args)
        {
            // Debug
            //args = new string[] { "base64", "" };

            // Кодировка вывода
            Console.OutputEncoding = Encoding.UTF8;
            
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

            #region Список файлов для загрузки на FTP/SFTP
            List<string> filesToUploadFtp = new List<string>();
            foreach (string localFile in Directory.GetFiles(BaseDir, "*", SearchOption.AllDirectories))
            {
                #region Локальный метод - "IsExcludeFolder"
                bool IsExcludeFolder()
                {
                    if (conf.Exclude != null)
                    {
                        foreach (string exclude in conf.Exclude.Split(','))
                        {
                            if (string.IsNullOrWhiteSpace(exclude?.Replace("/", "")))
                                continue;

                            if (localFile.Replace("\\", "/").Contains($"{conf.LocalFolder}/{exclude}"))
                                return true;
                        }
                    }

                    return false;
                }
                #endregion

                // Информация о файле
                FileInfo fileInfo = new FileInfo(localFile);

                // Высшая дата
                DateTime checkTime = fileInfo.LastWriteTime > fileInfo.CreationTime ? fileInfo.LastWriteTime : fileInfo.CreationTime;

                // Файл не изменился
                if (conf.LastSyncGood > checkTime)
                    continue;

                // Файлы в этой папки не нужно заливать
                if (IsExcludeFolder())
                    continue;

                filesToUploadFtp.Add(localFile);
            }

            // Выводим количиство файлов для загрузки на FTP/SFTP
            WriteLine(Methods.uploadStat, filesToUploadFtp.Count);
            #endregion

            #region Копируем файлы на FTP/SFTP
            int countUploadToErrorFiles = 0;
            List<string> filesToErrorUploadFtp = new List<string>();

            // Заливаем файлы
            ResetUpload: Parallel.ForEach(filesToUploadFtp, new ParallelOptions { MaxDegreeOfParallelism = (conf.type == "ftp" ? 1 : 10) }, localFile =>
            {
                // Создаем папку на FTP/SFTP
                CreateDirectory(conf.type, Path.GetDirectoryName(localFile), conf.FtpFolder);

                // Загуржаем файл на сервер
                if (!UploadFile(conf.type, localFile, localFile.Replace("\\", "/").Replace(BaseDir, conf.FtpFolder), conf.FtpFolder, countUploadToErrorFiles >= 3))
                    filesToErrorUploadFtp.Add(localFile);
            });

            // Отправляем на повторную загрузку
            if (3 > countUploadToErrorFiles && filesToErrorUploadFtp.Count > 0)
            {
                filesToUploadFtp = new List<string>();
                filesToUploadFtp.AddRange(filesToErrorUploadFtp);

                filesToErrorUploadFtp = new List<string>();
                countUploadToErrorFiles++;

                Task.Delay(1000 * 3);
                goto ResetUpload;
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
            Thread.Sleep(300);
            WriteLine(Methods.syncGood, SyncGood);
            WriteLine(Methods.lastSyncGood, LastSyncGood);

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
        /// <param name="remoteFolder"></param>
        static bool UploadFile(string type, string localFile, string remoteFile, string remoteFolder, bool showErrorUploadResult)
        {
            // Модель файла
            var md = new UploadModel() { remoteFile = remoteFile.Replace(remoteFolder, "") };

            try
            {
                // Системный файл
                bool IsCeronFile = Regex.IsMatch(Path.GetFileName(localFile), @"\.(ce|blu|html|css)$", RegexOptions.IgnoreCase);

                // Файл недоступен
                if (!File.Exists(localFile))
                    return true;

                // Считываем файл или открываем на чтение
                using (var localFileStream = IsCeronFile ? new MemoryStream(File.ReadAllBytes(localFile)) : (Stream)File.OpenRead(localFile))
                {
                    // Расположение локального файла
                    md.localFile = localFile.Replace("\\", "/").Replace(BaseDir, "");

                    // Загружаем файл
                    switch (type)
                    {
                        case "ftp":
                            ftp.Upload(localFileStream, remoteFile, FtpExists.Overwrite, 
                                progress: new Progress<double>(percent => WriteLine(Methods.progressUploadFile, new ProgressModel(md.localFile, remoteFile, percent)))
                            );
                            break;
                        case "sftp":
                            sftp.UploadFile(localFileStream, remoteFile, true);
                            break;
                    }

                    // Успех
                    md.uploadResult = true;
                }
            }

            #region SFTP Exception
            catch (SshConnectionException ex) {
                WriteException(ex.InnerException.Message);
            }
            catch (SftpPathNotFoundException ex) {
                WriteException(ex.InnerException.Message);
            }
            catch (SftpPermissionDeniedException ex) {
                WriteException(ex.InnerException.Message);
            }
            catch (SshException ex) {
                WriteException(ex.InnerException.Message);
            }
            #endregion

            #region FTP/SFTP Exception
            catch (FtpCommandException ex) {
                WriteException(ex.InnerException.Message);
            }
            catch (FtpException ex) {
                WriteException(ex.InnerException.Message);
            }
            #endregion

            #region Default Exception
            catch (Exception ex) {
                WriteException(ex.Message);
            }
            #endregion

            #region Локальный метод - "WriteException"
            void WriteException(string msg)
            {
                if (showErrorUploadResult)
                    SyncGood = false;

                md.uploadResult = false;
                md.errorMsg = msg;
            }
            #endregion

            // Выводим результат
            if (showErrorUploadResult || md.uploadResult)
                WriteLine(Methods.uploadFile, md);

            // Результат
            return md.uploadResult;
        }
        #endregion

        #region CreateDirectory
        /// <summary>
        /// 
        /// </summary>
        /// <param name="type"></param>
        /// <param name="localFolder"></param>
        /// <param name="remoteFolder"></param>
        static void CreateDirectory(string type, string localFolder, string remoteFolder)
        {
            string dirPath = "/";
            foreach (string DirName in localFolder.Replace("\\", "/").Replace(BaseDir, remoteFolder).Split('/'))
            {
                if (string.IsNullOrWhiteSpace(DirName))
                    continue;

                try
                {
                    dirPath += DirName + "/";
                    if (!CreateDirectorys.TryGetValue(dirPath, out _))
                    {
                        CreateDirectorys.Add(dirPath, 0);
                        if (!dirPath.Contains(remoteFolder))
                            continue;

                        switch (type)
                        {
                            case "ftp":
                                ftp.CreateDirectory(dirPath);
                                break;
                            case "sftp":
                                sftp.CreateDirectory(dirPath);
                                break;
                        }
                    }
                }
                catch { }
            }
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
