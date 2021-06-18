using Microsoft.Extensions.Configuration;
using ServiceStack;
using ServiceStack.Redis;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace Zhaoxi.LockService
{

	public class Userinfo
	{
		public int ID { get; set; }

		public string Name { get; set; }
	}

	class Program
	{
		static void Main(string[] args)
		{
			////命令行参数启动  秒杀，到一个时刻的时候，开始秒杀
			////dotnet Zhaoxi.LockService.dll --minute=21
			var builder = new ConfigurationBuilder().AddCommandLine(args);
			var configuration = builder.Build();
			int minute = int.Parse(configuration["minute"]);
			using (var client = new RedisClient("127.0.0.1", 6379))
			{
				//先把库存数量预支进去
				client.Set<int>("inventoryNum", 10);
				//订单  如果订单是10 或者<=10 说明没有问题
				client.Set<int>("orderNum", 0);
			}
			Console.WriteLine($"在{minute}分0秒正式开启秒杀！");
			var flag = true;
			while (flag)
			{
				if (DateTime.Now.Minute == minute)
				{
					flag = false;
					Parallel.For(0, 30, (i) =>
					{
						Task.Run(() =>
						{
							//1、redis单节支持，redis集群不支持，且有并发性能问题
							//NormalSecondsKill.Show();
							#region MyRegion

							//2、阻塞锁--单节点redis   
							//拿不到锁的时候，还有等待，等待时间都交给客户端程序了，有并发性能问题
							//BlockingLock.Show(i, "akey", TimeSpan.FromSeconds(100));

							//3、非阻塞锁(自己手写实现的)--单节点redis   
							// 把这个补偿重试，能不能交给用户，，， 浏览器，， 你想要重试，就重试，不想要，
							// 如果拿到锁，就返回true，如果拿不到，就false---
							//ImmediatelyLock.Show(i, "akey", TimeSpan.FromSeconds(100));

							//4、宏锁--redis集群（官方推荐：https://github.com/KidFashion/redlock-cs）
							RadLockSkill.Show(i, "akey", TimeSpan.FromSeconds(100));
							#endregion
						});
					});
					Console.ReadKey();
				}
			}
		}
	}
}
