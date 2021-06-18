using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using ServiceStack.Redis;
namespace Zhaoxi.LockService
{
	public class BlockingLock
	{
		//这个是阻塞的锁
		public static void Show(int i, string key, TimeSpan timeout)
		{
			using (var client = new RedisClient("127.0.0.1", 6379))
			{
				//timeout : 锁的占有时间，这个时间不是redis里面的过期时间，就是写进去一个值。
				//timeout : 如果拿不到锁，我需要自身轮询等待多久，先去判断能不能拿到锁，如果拿不到，等多久，如果等多久之后，还拿不到，则直接不会执行里面的代码。。
				using (var datalock = client.AcquireLock("DataLock:" + key, timeout))
				{
					// 首先判断这个DataLock的key不在，说明没有人拿到锁，我就可以写一个key:DataLock,values=timeout
					// 如果这个key存在，判断时间过没过期，如果过期了，则我们可以拿到锁，如果没有，则进行根据你的等待时间在去轮询判断锁在不在
					// 如果多个线程在判断锁的时候，发现锁没有过期，然后一直等待

					// AcquireLock 底层
					// 刚好三个线程，并发同一时间，发getredis请求，判断这个key,此时此刻，刚好，这个key不在，会不会出现，三个人都去set key ，大不了把值覆盖，但是可能这个三个线程都拿到锁。。。 
					// 判断这个锁在不在时候，都会查询出当前的key，还有我们的版本号
					// 如果锁过期。然后在抢锁的过程--用事务的版本--- 带着版本号提交，如果版本号一致，则拿到锁，如果不一致，那锁失败 
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
						Console.WriteLine($"{i}抢购失败");
						Thread.Sleep(100);
					}
				}//释放datalock锁

				//解决方案：
				//timeout时间问题：需要大量测试时间间隔，可以考虑间隔时间设置大些
				//1、正常情况下
				//比如第一个线程进来拿到了锁，时间间隔是10s，设置的超时时间是2021-06-05 20:27:50，如果第一个线程执行的比较快，在10s内执行完了，
				//那第二线程个就顺利拿到锁，假如第二个线程设置超时时间为2021-06-05 20:27:60，以此内推...正常执行
				//2、非正常情况
				//比如第一个线程进来拿到了锁，时间间隔是10s，设置的超时时间是2021-06-05 20:27:50，如果第一个线程执行超时后，已经超过了执行10s时间，
				//那第二线程来拿锁的时候，发现已经前面一个线程已经超时了，就会顺利拿到锁执行它的任务，但是此时第一个线程又执行完了他的任务，就会把第二个线程的锁给释放掉，
				//此时第二个线程还在执行任务，导致第三个线程就会顺利拿到锁，以此类推...异常执行
			}
		}
	}
}