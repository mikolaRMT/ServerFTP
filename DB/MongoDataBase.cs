using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Bson.Serialization;
using System;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;


namespace ServerFTP.DB
{
    internal class MongoDataBase
    {
        static MongoClient connection = new MongoClient("mongodb://localhost:27017");
        static IMongoDatabase database = connection.GetDatabase("Aboba");
        static IMongoCollection<BsonDocument> users = database.GetCollection<BsonDocument>("users");
        public static bool LoginUser(string login, string password)
        {
            var filter = new BsonDocument { { "login", login }, { "password", password } };
            var UserList = users.Find(filter).ToList();
            if (UserList.Count == 0)
                return false;
            else
                return true;
        }
        public static void AddUser(BsonDocument user)
        {
            users.InsertOne(user);
        }

        public static bool UserLoginIsTaken(string login)
        {
            var filter = new BsonDocument { { "login", new BsonDocument("$eq", login) } };
            var UserList = users.Find(filter).ToList();
            if (UserList.Count == 0)
                return false;
            else
                return true;
        }

        public static bool UserAdmin(string login)
        {
            var filter = new BsonDocument { { "login", login }, { "isAdmin", true} };
            var UserList = users.Find(filter).ToList();
            if (UserList.Count == 0)
                return false;
            else
                return true;
        }

        public static int UserPerm(string login)
        {
            var filter = new BsonDocument { { "login", login }};
            var UserList = users.Find(filter).ToList();
            var user = UserList[0];
            return Convert.ToInt32(user["PermLvl"]);
        }
    }
    class User
    {
        public ObjectId Id;
        public string? userLogin { get; set; }
        public string? userPassword { get; set; }
        
        public bool isAdmin;
        public int permLvl;
        //1 - Чтение файлов
        //2 - Скачивание файлов или директорий
        //3 - Загрузка и создание файлов или директорий
        //4 - Удаление файлов или директорий

        private bool debug = false;
        
        //public MongoDataBase aBobase = new MongoDataBase();

        private string GetHash(string userPassword)
        {
            var md5 = MD5.Create();
            var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(userPassword));
            return Convert.ToBase64String(hash);
        }

        public bool UserRegistration(string userLogin, string userPassword, bool Admin = false, int Perm = 1)
        {
            if (MongoDataBase.UserLoginIsTaken(userLogin))
                    return false;
            
            userPassword = GetHash(userPassword);
            BsonDocument user = new BsonDocument{
            { "login", userLogin },
            { "password", userPassword},
            { "isAdmin", Admin },
            { "PermLvl", Perm}
            };
            MongoDataBase.AddUser(user);
            return true;
        }

        public bool UserLoginingLogin(string userLogin)
        {
            if ( userLogin == "anonymous")
            {
                this.userLogin = userLogin;
                permLvl = 2;
                return true;
            }
            if (!debug)
            {
                if (!MongoDataBase.UserLoginIsTaken(userLogin))
                    return false;
                this.userLogin = userLogin;
                return true;
            }
            else
                return true;
        }
        public bool UserLogin(string userPassword)
        {
            if (userLogin == "anonymous")
            {
                permLvl = 2;
                return true;
            }
            if (!debug)
            {
                userPassword = GetHash(userPassword);
                if (MongoDataBase.LoginUser(userLogin, userPassword))
                {
                    this.userPassword = userPassword;
                    isAdmin = MongoDataBase.UserAdmin(userLogin);
                    if (isAdmin)
                        permLvl = 4;
                    else
                    {
                        permLvl = MongoDataBase.UserPerm(userLogin);
                    }
                    return true;
                }
                return false;
            }
            else
            {
                isAdmin = true;
                permLvl = 4;
                return true;
            }
        }
    }
}




