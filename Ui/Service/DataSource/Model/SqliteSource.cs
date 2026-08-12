using System;
using System.IO;
using _1RM.Service.DataSource.DAO.Dapper;
using _1RM.Service.DataSource.DAO;
using Newtonsoft.Json;
using Shawn.Utils;

namespace _1RM.Service.DataSource.Model
{
    public sealed partial class SqliteSource : DataSourceBase
    {
        public readonly string Name;
        private readonly IDatabase _database;

        private string _path = "";
        public string Path
        {
            get => _path;
            set
            {
                var newValue = value.Replace(Environment.CurrentDirectory, ".");
                SetAndNotifyIfChanged(ref _path, newValue);
                var fi = new FileInfo(Path);
                IsWritable = fi.IsReadOnly == false;
            }
        }

        public SqliteSource(string name) : base()
        {
            Name = name;
            _database = new DapperDatabaseFree(Name, DatabaseType);
            SimpleLogHelper.Debug(nameof(SqliteSource) + " Construct: " + this.GetHashCode());
        }

        // No finalizer. It only wrote a debug line, and the cost of that was making every instance survive an
        // extra GC generation and putting a log call on the finalizer thread.

        public override string GetConnectionString(int connectTimeOutSeconds = 5)
        {
            return DbExtensions.GetSqliteConnectionString(Path);
        }

        [JsonIgnore]
        public override DatabaseType DatabaseType => DatabaseType.Sqlite;
        [JsonIgnore]
        public override string Description => Path;



        public override IDatabase GetDataBase()
        {
            return _database;
        }
    }
}
