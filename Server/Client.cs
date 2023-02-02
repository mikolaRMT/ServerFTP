using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using ServerFTP.DB;
using System.Net;

namespace ServerFTP.Server
{
	class Client
	{
		public Client(TcpClient client)
		{
			this.client = client;
			stream = client.GetStream();
			endPoint = client.Client.RemoteEndPoint;
		}

		public User user = new User();
		public string PermLvl;

		static int p1 = 10;
		static int p2 = 1;
		public List<string> serverTasks = new List<string>();
		public NetworkStream stream;
		public TcpClient client;
		public TcpClient ftc;
		public EndPoint endPoint = null;
		TcpListener listener;
		public bool isActive = false;
		public bool workEnd = false;
		public bool utfMode = false;
		public bool isAuth = false;
		public string nickName = "";
		public string dir = "/";
		string rest = "";
		//public IPEndPoint ipEndPoint;

		public string Recive()
		{
			byte[] msgArr = new byte[1024];
			int temp;
			stream.ReadTimeout = 300000;
			try
			{
				if(stream.Read(msgArr, 0, 1024) == 0)
				{
					Log.Debug($"Пользователь {endPoint} разорвал соединение.");
					{
						workEnd = true;
						stream.Close();
						return "";
					}
				}
			}
			catch (IOException)
			{
				Log.Debug("Превышено время ожидания.");
				workEnd = true;
				stream.Close();
				return "";
			}

			string msg;
			if (utfMode)
				msg = Encoding.UTF8.GetString(msgArr);
			else
				msg = Encoding.ASCII.GetString(msgArr);

			msg = rest + msg;
			rest = "";

			Log.Debug("Получено от клиента {0}: {1}", nickName, msg);
			msg = msg.Replace("\0", string.Empty);
			return msg;
		}

		public void Send(string msg)
		{
			if (!client.Connected)
			{
				workEnd = true;
				return;
			}
			msg += "\r\n";

			byte[] msgArr = EncToByte(msg);

			// Send the message to the connected TcpServer.
			stream.Write(msgArr, 0, msgArr.Length);
			Log.Debug("Сообщение клиенту {0}: {1}", nickName, msg);
		}

		public async void SendFile(string dir, string fileName, bool isLs = false)
		{
			await Task.Run(() =>
			{
				Send("150 Ready");
				NetworkStream netStream = null;
				try
				{
					netStream = ftc.GetStream();

					byte[] data;
					string ls = "";
					if (isLs == true)
					{
						DirectoryInfo di = new DirectoryInfo(dir + this.dir);
						
						foreach (var diri in di.GetDirectories())
						{
							ls += diri.CreationTime.ToString() + '\t';
							ls += diri.Attributes.ToString() + '\t';
							ls += diri.Name + '\n';
						}
						foreach (var fi in di.GetFiles())
						{
							ls += fi.CreationTime.ToString() + '\t';
							ls += fi.Attributes.ToString() + '\t' + '\t';
							ls += fi.Name + '\n';
						}

						data = EncToByte(ls);
					}
					else
					{
						ls = fileName;
						if (!File.Exists($"{dir}/{this.dir}/{fileName}"))
						{
							Send("550 Invalid name ");
							return;
						}
						data = File.ReadAllBytes($"{dir}/{this.dir}/{fileName}");
					}

					MemoryStream ms = new MemoryStream(data);
					netStream.Write(ms.ToArray());
					Log.Debug("Отправленны данные клиенту {0}:\n{1}", nickName, ls);

					Send("226 Transfer completed");
				}
				catch (Exception ex)
				{
					if (!ftc.Connected)
						Log.Debug($"Пользователь {endPoint} разорвал соединение для передачи данных.");
					else
						Log.Debug("Превышено время ожидания пердачи данных.");

					Log.Error(ex.ToString());
					Send("550 Transfer error");
				}
				finally 
				{
					netStream.Close();
					ftc.Close();
				}
			});
		}


		public async void Retrieve(string dir, string fileName)
		{
			await Task.Run(() =>
			{
				Send("150 Ready");
				NetworkStream netStream = null;
				try
				{
					netStream = ftc.GetStream();

					netStream.ReadTimeout = 300000;
					try
					{
						MemoryStream ms = new MemoryStream();
						netStream.CopyTo(ms);
						byte[] data = ms.ToArray();

						File.WriteAllBytes($"{dir}/{this.dir}/{fileName}", data);
						Send("226 Transfer completed");
						Log.Debug("Получены данные от клиента {0}: {1}", nickName, fileName);
					}
					catch (IOException)
					{
						if (!ftc.Connected)
							Log.Debug($"Пользователь {endPoint} разорвал соединение для передачи данных.");
						else
							Log.Debug("Превышено время ожидания пердачи данных.");

						Send("550 Transfer error");
					}
					finally
					{
						netStream.Close();
						ftc.Close();
					}
				}
				catch (Exception ex)
				{
					Log.Error(ex.ToString());
				}
			});
		}

		public bool ActivMode(string ipAndPort)
		{
			isActive= true;
			ftc = new TcpClient();
			string[] temp = ipAndPort.Split(',');
			string ip = "";
			int port;
			for (int i = 0; i < 4; i++)
			{
				ip += temp[i];
				if (i != 3)
					ip += '.';
			}
			port = int.Parse(temp[4]) * 256 + int.Parse(temp[5]);
			try
			{
				ftc.Connect(ip, port);
				return true;
			}
			catch (Exception ex)
			{
				Log.Error(ex.ToString());
				return false;
			}
		}

		public byte[] EncToByte(string data)
		{
			if (utfMode)
				return Encoding.UTF8.GetBytes(data);
			else
				return Encoding.ASCII.GetBytes(data);
		}

		public bool PassiveMode()
		{
			isActive = false;
			listener = new TcpListener(IPAddress.Any, p1 * 256 + p2);
			Send($"227 Passive mode ON. 26,122,69,5,{p1},{p2}");
			p1++;
			p2++;

			listener.Start();

			while (true)
			{
				if (listener.Pending())
				{
					ftc = listener.AcceptTcpClient();
					listener.Stop();
					break;
				}
			}
			return true;
		}
	}
}
