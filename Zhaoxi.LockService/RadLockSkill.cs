using Redlock.CSharp;
using ServiceStack.Redis;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;


namespace Zhaoxi.LockService
{
	/// <summary>
	/// 官方推荐宏锁
	/// </summary>
	public class RadLockSkill
	{
		//docker run -d  -p 6379:6379 --name myredis redis 
		//docker run -d  -p 6380:6379  --name myredis1 redis 
		//docker run -d  -p 6381:6379  --name myredis2  redis 
		public static void Show(int i, string key, TimeSpan timeout)
		{
			//redis集群
			var dlm = new Redlock.CSharp.Redlock(ConnectionMultiplexer.Connect("192.168.1.211:6379"), ConnectionMultiplexer.Connect("192.168.1.211:6380"), ConnectionMultiplexer.Connect("192.168.1.211:6381"));
			Lock lockObject;
			// true拿到锁,false拿不到、阻塞锁（内部还是补偿重试）
			var isLocked = dlm.Lock(key, timeout, out lockObject);
			if (isLocked)
			{
				try
				{
					using (var client = new RedisClient("192.168.1.211", 6379))
					{
						//库存数量
						var inventory = client.Get<int>("inventoryNum");
						if (inventory > 0)
						{
							//库存-1
							client.Set<int>("inventoryNum", inventory - 1);
							//订单+1
							var orderNum = client.Incr("orderNum");
							Console.WriteLine($"{i}抢购成功*****线程id：{ Thread.CurrentThread.ManagedThreadId.ToString("00")},库存：{inventory},订单数量：{orderNum}");
						}
						else
						{
							Console.WriteLine($"{i}抢购失败:原因，没有库存");
						}
					}
				}
				catch
				{
					throw;
				}
				finally
				{
					dlm.Unlock(lockObject);//删锁 
				}
			}
			else
			{
				Console.WriteLine($"{i}抢购失败：原因：没有拿到锁");
			}
		}
	}
}
