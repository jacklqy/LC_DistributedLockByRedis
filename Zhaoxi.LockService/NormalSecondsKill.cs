using ServiceStack.Redis;
using System;
using System.Threading;
namespace Zhaoxi.LockService
{
    public class NormalSecondsKill
	{
		public static readonly object olock = new object();
		//单节点下没有问题，如果是多节点集群情况下就有问题
		public static void Show()
		{

			// 异步代码，代码如果是调式的话，肯定是没有顺序
			// 能不能把代码变成同步代码。让代码执行有顺序。。
			using (var client = new RedisClient("127.0.1.1", 6379))
			{
				// 把多线程的代码变成了单线程
				// lock 必须存储引用类型，数组，对象，特殊字符串
				//lock (olock)
				//加锁
				Monitor.Enter(olock);
				// 判断mysql数据库中，字段的标识 0 1，如果等0 ，补充重试---不断判断等这个标识=0，我可以改成1，然后继续往下进行。。
				var inventory = client.Get<int>("inventoryNum");
				if (inventory > 0)
				{
					// 库存-1
					client.Set<int>("inventoryNum", inventory - 1);
					// 订单+1
					var orderNum = client.Incr("orderNum");
					Console.WriteLine($"抢购成功*****线程id：{ Thread.CurrentThread.ManagedThreadId.ToString("00")},库存：{inventory},订单数量：{orderNum}");
				}
				else
				{
					Console.WriteLine("抢购失败");
				}
				//释放锁
				Monitor.Exit(olock);
			}
		}
	}
}

