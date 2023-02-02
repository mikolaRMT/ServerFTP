using System.Threading;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Net;
using System.Net.Http;
using System.IO;
using static ServerFTP.DB.MongoDataBase;
using MongoDB.Bson;
using MongoDB.Driver;
using ServerFTP.DB;
using Serilog;
using System.Text;
using System.Security.Cryptography;
using System.Reflection;
using Microsoft.VisualBasic.FileIO;
using System;

namespace ServerFTP.Server
{
	class Server
	{
		TcpListener listener = new TcpListener(IPAddress.Any, 2009);
		List<Client> clients = new List<Client>();
		private string dir = Directory.GetParent(Directory.GetCurrentDirectory()).Parent.Parent.FullName + "/Storage";
		private string renameFile = "";
		private string currentDir;
		bool stopServer = false;

		public void Start()
		{
			currentDir = dir;
			Log.Debug("Сервер запущен.");
			listener.Start();
			Stop();
			while (!stopServer)
			{
				try
				{
					if (listener.Pending())
					{
						Thread aThread = new Thread(() => { ClientProcesing(listener.AcceptTcpClient()); });
						aThread.IsBackground = true;
						aThread.Start();
					}
				}
				catch (ObjectDisposedException ex)
				{
					Log.Debug($"Причина разрыва: {ex.Message}.");
				}
			}
			
			foreach (var client in clients)
			{
				Log.Debug($"Пользователь {client.endPoint} отключен."); 
				client.client.Close();
			}
			listener.Stop();
			Environment.Exit(Environment.ExitCode);
		}

		private void ClientProcesing(TcpClient _client)
		{
            Client client = new Client(_client);
			clients.Add(client);
            Log.Debug($"Входящее подключение: {client.endPoint}");	

			client.Send("220-Welcome to FTP Server");
			client.Send("220 You will be disconnected after 5 minutes of inactivity.");
			while (!client.workEnd)
			{
				try
				{
					Commands(client);
				}
				catch (Exception ex)
				{
					Log.Error(ex.ToString());
				}
			}
			Log.Debug($"Пользователь {client.endPoint} отключен.");
			client.client.Close();
			clients.Remove(client);
        }

		private void Commands(Client client)
		{
			string[] commands = client.Recive().Split('\r','\n');
			
			bool flag;
			for (int i = 0; i < commands.Length; i++)
			{
				string[] command = commands[i].Split(" ");
				if (commands[i] == "")
					continue;
				flag = true;

				command[0] = command[0].ToUpper();

				switch (command[0])
				{
					case "USER":
						if (command.Length < 2)
						{
							client.Send("440 Command: " + command[0] +" must contain the parameter.");
							continue;
						}
                        try
						{
							if(client.user.UserLoginingLogin(command[1]))
							{
                                if (command[1] == "anonymous")
                                {
                                    client.Send("230 User " + command[1] + " has access level: " + client.user.permLvl);
                                    continue;
                                }
                                client.Send("331 User " + command[1] + " OK. Password required.");
								client.nickName = command[1];
							}
							else
							{
								client.Send("User " + command[1] + " not found.");
							}
						}
						catch (TimeoutException ex)
						{
							Log.Error(ex, "Something went wrong");
							throw;
						}
						
						break;

					case "PASS":
                        if (command.Length < 2)
						{
							client.Send("440 Command: " + command[0] +" must contain the parameter.");
							continue;
						}
                        if (command[1] == "anonymous")
                        {
                            client.Send("202-User " + client.nickName + " has access level: " + client.user.permLvl);
							continue;
                        }
                        if (client.user.UserLogin(command[1]))
						{
							client.Send("230-User "+ client.nickName +
								" has access level: " + client.user.permLvl);//добавить список доступных дирректорий
							client.Send("230 OK. Current restricted directory is /");
						}
						break;

					case "DELE"://Удалить файл
						if (command.Length < 2)
						{
							client.Send("440 Command: " + command[0] +" must contain the parameter.");
							continue;
						}
						if (client.user.permLvl > 3)
						{
							if (DeleteFile(client, command[1]))
								client.Send($"File was successfully deleted.");
							else
								client.Send("440 Cannot delete given file.");
                        }
						else
						{
							client.Send($"440 Access denied, level required 4. You permition level: {client.user.permLvl}.");
						}
						break;

					case "CWD"://Перейти в директорию
						if (command.Length < 2)
						{
							client.Send("440 Command: " + command[0] +" must contain the parameter.");
							continue;
						}
						if (client.user.permLvl > 0)
						{
							if (GoToNewDirectory(client, command[1]))
								client.Send("210 Done");
							else
								client.Send("440 Can not get given directory");

                        }
						else
						{
							client.Send($"440 Access denied, level required 1. You permition level: {client.user.permLvl}.");
						}
						break;

					case "MKD":
					case "XMKD"://Создать директорию
						if (command.Length < 2)
						{
							client.Send("440 Command: " + command[0] +" must contain the parameter.");
							continue;
						}
						if (client.user.permLvl > 2)
						{
							if (CreateDir(command[1]))
								client.Send($"210 Direction {command[1]} successfully created.");
							else
								client.Send("440 Can not create directory.");
						}
						else
						{
							client.Send($"440 Access denied, level required 3. You permition level: {client.user.permLvl}.");
						}
						break;

					case "RNFR"://Выбрать файл для переименования
						if (command.Length < 2)
						{
							client.Send("440 Command: " + command[0] +" must contain the parameter.");
							continue;
						}
						if (client.user.permLvl > 2)
						{
							if (flag == ChooseRenameFile(command[1]))
								client.Send($"350 File {renameFile} has been selected for renaming.");
							else
								client.Send("440 File with this name doesn't exist.");
                        }
						else
						{
							client.Send($"440 Access denied, level required 3. You permition level: {client.user.permLvl}.");
						}
						break;

					case "RNTO"://Переименовать файл
						if (command.Length < 2)
						{
							client.Send("440 Command: " + command[0] +" must contain the parameter.");
							continue;
						}
						if (client.user.permLvl > 2 && renameFile != "")
						{
							if (!flag)
							{
								client.Send("440 file for renaming is not selected.");
								break;
							}
							RenameFile(command[1]);
                            client.Send($"220 File {renameFile} successfully renamed.");
						}
						else
						{
							client.Send($"440 Access denied, level required 3. You permition level: {client.user.permLvl}.");
						}
						break;

					case "RMD":
					case "XRMD"://Удалить директорию
						if (client.user.permLvl > 3)
						{
							string send = client.dir;
							if (command.Length > 1)
							{
								flag = DeleteDir(client, command[1]);

								if (send == "/")
									send += command[1];
								else
									send += "/" + command[1];
							}
							else
								flag = DeleteDir(client);

							if (flag)
								client.Send($"Directory: {send} was successfully deleted.");
							else
								client.Send("440 Can not delete given directory.");
						}
						else
						{
							client.Send($"440 Access denied, level required 4. You permition level: {client.user.permLvl}.");
						}
						break;

					case "PWD"://Узнать текущую директорию
					case "XPWD":
						//client.Send($"210 {dir}{client.dir}");
						client.Send($"210 Current dir: {client.dir} \n");
						break;

					case "QUIT"://Закончить работу
						client.Send("Exit........");
						client.workEnd = true;
						break;

					case "LIST":
					case "NLST"://Возврат всех директорий и файлов
						client.SendFile(dir, "", true);
						break;

					case "PORT":
						if (client.ActivMode(command[1]))
							client.Send("229 Active mode ON");
						else
							client.Send("425 Failed to connect");
						break;

					case "HELP"://Узнать список команд          
						client.Send("210-\n =========== \n" +
						            "List of commands: \n" +
						            "LS - Provide information about directories and files \n" +
						            "CWD - Go to directory \n" +
						            "MKD - Create directory \n" +
						            "RMD - Delete directory \n" +
                                    "DELE - Delete file \n" +
                                    "RNFR - Select file to rename \n" +
						            "RNTO - Rename file \n" +
						            "CD - Go up \n" +
						            "QUIT - Quit from session \n" +
									"210 =========== \n");
						break;

					case "OPTS":
						if (command[1] == "UTF8")
						{
							if (command[2] == "ON")
							{
								client.utfMode = true;
								client.Send("200 UTF8 mode on");
							}
							else if (command[2] == "OFF")
							{
								client.utfMode = false;
								client.Send("200 UTF8 mode off");
							}
						}
						break;

					case "RETR":
						if (client.user.permLvl > 1) 
							client.SendFile(dir, command[1]);
                        else
                            client.Send($"440 Access denied, level required 2. You permition level: {client.user.permLvl}.");
                        break;

					case "STOR":
                        if (client.user.permLvl > 2) 
                            client.Retrieve(dir,command[1]);
                        else
                            client.Send($"440 Access denied, level required 3. You permition level: {client.user.permLvl}.");
                        break;

					case "SYST":
						client.Send("215 Тип UNIX: L8");
						break;

					case "TYPE":
						if (command[1].ToUpper() == "I")
							client.Send("200 TYPE I ON");
						else if (command[1].ToUpper() == "A")
							client.Send("200 TYPE I OFF");
						break;

					case "PASV":
						client.PassiveMode();
						break;

					default:
						client.Send("101 incorrect command:" + commands[i]);
                        break;
				}
            }
		}

		private bool DeleteDir(Client client, string path = "")
		{
			if (path == "" && currentDir != dir)
            {
                Directory.Delete(currentDir, true);
                currentDir = currentDir.Substring(0, currentDir.LastIndexOf("/"));

				if (client.dir.LastIndexOf("/") == 0)
					client.dir = client.dir.Substring(0, client.dir.LastIndexOf("/") + 1);
				else
					client.dir = client.dir.Substring(0, client.dir.LastIndexOf("/"));
			}
            else if (path!="" && Directory.Exists(currentDir + "/" + path))
				Directory.Delete(currentDir + "/" + path, true);
			else
				return false;
            return true;
        }

		private bool DeleteFile(Client client, string fileName)
		{

			if (File.Exists(currentDir + "/" + fileName))
			{
				FileInfo a = new FileInfo(currentDir + "/" + fileName);
				a.Delete();
			}
			else
                return false;
            return true;
		}

		private bool CreateDir(string dirName)
		{
			if (Directory.Exists(currentDir + "/" + dirName))
				return false;
			else
				Directory.CreateDirectory(currentDir + "/" + dirName);
			return true;
		}

		private bool ChooseRenameFile(string choosenFile)
		{
			if(File.Exists(currentDir+"/"+choosenFile))
			{
				renameFile = choosenFile;
                return true;
            }
			return false;
		}

		private void RenameFile(string newName)
		{
			File.Move(currentDir + "/" + renameFile, currentDir + "/" + newName);
			renameFile = "";
		}

		private bool GoToNewDirectory(Client client, string dirName)
		{
			if (dirName == "..")
				return GoUp(client);

			if (dirName == $"{currentDir}{client.dir}" || $"{currentDir}/{dirName}" == $"{currentDir}{client.dir}")
				return true;

			if (Directory.Exists(currentDir + "/" + dirName))
			{
				currentDir += "/" + dirName;
				if (client.dir == "/")
					client.dir += dirName;
				else
					client.dir += "/" + dirName;
				return true;
            }
			return false;
		}

		private bool GoUp(Client client)
		{
			if (currentDir == dir)
				return false;
			else
			{
				currentDir = currentDir.Substring(0, currentDir.LastIndexOf("/"));

				if (client.dir.LastIndexOf("/") == 0)
					client.dir = client.dir.Substring(0, client.dir.LastIndexOf("/") + 1);
				else
					client.dir = client.dir.Substring(0, client.dir.LastIndexOf("/"));
				return true;
			}
		}

		async private void Stop()
		{
			await Task.Run(() =>
			{
				while (true)
				{
					if (Console.ReadLine() == "~")
					{
						Log.Debug("Начинаем отключение сервера.");
						stopServer = true;
						return;
					}
				}
				
			});
		}

	}

	internal class Program
	{
		private static void RegNewUser()
		{
			User aboba = new User();
			aboba.UserRegistration("andrey", "1", false, 1);
			//aboba.UserLoginingLogin("Aboba");
			//aboba.UserLogin( "123");
			//Console.WriteLine(aboba.PermLvl);
		}
		static void Main(string[] args)
		{
			//RegNewUser();
			Server server = new Server();

			Log.Logger = new LoggerConfiguration()
						   .MinimumLevel.Debug()
						   .WriteTo.Console()
						   .WriteTo.File("logs/.txt", rollingInterval: RollingInterval.Day)
						   .CreateLogger();
			try
			{
				server.Start();
			}
			catch (Exception ex)
			{
				Log.Error(ex, "Something went wrong");
			}
			finally
			{
				Log.CloseAndFlush();
			}
		}
	}
}


