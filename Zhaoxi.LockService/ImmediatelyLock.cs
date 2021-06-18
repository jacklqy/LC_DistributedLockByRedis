using ServiceStack.Redis;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Zhaoxi.LockService
{
	public class ImmediatelyLock
	{
		//非阻塞锁--自己手写实现的
		public static void Show(int i, string key, TimeSpan timeout)
		{
			using (var client = new RedisClient("127.0.0.1", 6379))
			{
				// true ,拿到锁,false 拿不到timeout 它就是操作redis的时候，设置过期时间，交给redis
				// 就自己写的， 非阻塞锁。。。而且也不会出现，删除别的锁---
				// 有一个问题，如果时间时间设置太多，，， 10s 
				// 续命，，子线程 ，循环-- 标识-- 1  9 +13 --执行完成--改标识，就不要在去循环做续命的事情。。。
			   // 如果页面哪里真的遇到了问题，没有办法执行下去，其他客户端一直等，一直等。。。 数据，业务，那边真的出现问题，，特殊数据出现的问题。。 死锁，，少不了人工参与-- 也用的比较 
				bool isLocked = client.Add<string>("DataLock:" + key, key, timeout);
				if (isLocked)
				{
					try
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
					catch
					{
						throw;
					}
					finally
					{
						//如果没有到过期时间，而且自己也执行完了，是不是要删。。
						//如果自己的锁的过期 12
						client.Remove("DataLock:" + key);
					}
				}
				else
				{
					Console.WriteLine($"{i}抢购失败：原因：没有拿到锁");
				}
			}
		}
	}
}
