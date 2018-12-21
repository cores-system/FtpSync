namespace FtpSync.Models
{
    public enum Methods
    {
        errorMsg,      // Синхронизация не запущена из за ошибки 
        uploadFile,    // Данные загруженого файла, статус, папки и т.д
        syncGood,      // Если false то один из файлов не загрузился
        lastSyncGood,  // Время успешной отметки
        debug
    }
}
