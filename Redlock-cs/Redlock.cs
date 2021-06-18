#region LICENSE
/*
 *   Copyright 2014 Angelo Simone Scotto <scotto.a@gmail.com>
 * 
 *   Licensed under the Apache License, Version 2.0 (the "License");
 *   you may not use this file except in compliance with the License.
 *   You may obtain a copy of the License at
 * 
 *       http://www.apache.org/licenses/LICENSE-2.0
 * 
 *   Unless required by applicable law or agreed to in writing, software
 *   distributed under the License is distributed on an "AS IS" BASIS,
 *   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 *   See the License for the specific language governing permissions and
 *   limitations under the License.
 * 
 * */
#endregion

using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Redlock.CSharp
{
    public class Redlock
    {

        // 连接多个节点的redis服务
        public Redlock(params IConnectionMultiplexer[] list)
        { 
            foreach(var item in list)
                this.redisMasterDictionary.Add(item.GetEndPoints().First().ToString(),item);
        }
        //默认重试次数
        const int DefaultRetryCount = 3;
        //默认每次重试等待时间200毫秒
        readonly TimeSpan DefaultRetryDelay = new TimeSpan(0, 0, 0, 0, 200);
        //时钟驱动因子
        const double ClockDriveFactor = 0.01;
        // 节点的大多数数量
        protected int Quorum { get { return (redisMasterDictionary.Count / 2) + 1; } }

        /// <summary>
        ///释放锁的脚本//   lua  提高性能//一次性执行 
        /// </summary>
        const String UnlockScript = @"
            if redis.call(""get"",KEYS[1]) == ARGV[1] then
                return redis.call(""del"",KEYS[1])
            else
                return 0
            end";

        //客户端抢锁的唯一标识,全局唯一
        protected static byte[] CreateUniqueLockId()
        {
            return Guid.NewGuid().ToByteArray();
        }


        protected Dictionary<String,IConnectionMultiplexer> redisMasterDictionary = new Dictionary<string,IConnectionMultiplexer>();

        //构造连接resis实例
        protected bool LockInstance(string redisServer, string resource, byte[] val, TimeSpan ttl)
        {
            
            bool succeeded;
            try
            {    
                // 去操作一个节点 
                var redis = this.redisMasterDictionary[redisServer];
                // 设置过期时间，交给redis，value=guid 
                succeeded = redis.GetDatabase().StringSet(resource, val, ttl, When.NotExists);
            }
            catch (Exception)
            {
                succeeded = false;
            }
            return succeeded;
        }

        //释放锁方法执行释放锁的lua脚本,在每一个服务器上都执行
        protected void UnlockInstance(string redisServer, string resource, byte[] val)
        {
            RedisKey[] key = { resource };
            RedisValue[] values = { val };
            var redis = redisMasterDictionary[redisServer];
            redis.GetDatabase().ScriptEvaluate(
                UnlockScript, 
                key,
                values
                );
        }
        //抢锁,如果成功返回true,否则返回失败
        public bool Lock(RedisKey resource, TimeSpan ttl, out Lock lockObject)
        {
            //生成抢锁前的唯一字符串
            var val = CreateUniqueLockId();
            Lock innerLock = null;
            //补充重试多次抢锁
            bool successfull = retry(DefaultRetryCount, DefaultRetryDelay, () =>
            {
                try
                {
                    int n = 0;
                    var startTime = DateTime.Now;

                    // 抢锁 调用了我们开始配置的多个节点reids，一个一个操作
                    // 给三个节点同时发送抢锁的key
                    for_each_redis_registered(
                        redis =>
                        {
                            // 累加当前我这个客户端，有几个节点拿到锁。。。
                            if (LockInstance(redis, resource, val, ttl)) n += 1;
                        }
                    );

                    /*
                     * Add 2 milliseconds to the drift to account for Redis expires
                     * precision, which is 1 millisecond, plus 1 millisecond min drift 
                     * for small TTLs.        
                     */
                    var drift = Convert.ToInt32((ttl.TotalMilliseconds * ClockDriveFactor) + 2);
                    // 计算抢到锁花费的时间和设置的过过期时间

                    // 业务，是设置占有5s,,你在抢锁的过程中，超过了5s 
                    var validity_time = ttl - (DateTime.Now - startTime) - new TimeSpan(0, 0, 0, 0, drift);
                    //如果大部分节点抢到了锁,而且抢锁花费的时间小于设置的锁过期时间
                    if (n >= Quorum && validity_time.TotalMilliseconds > 0)
                    {
                        //生成锁,返回true
                        innerLock = new Lock(resource, val, validity_time);
                        return true;
                    }
                    else
                    {
                        // 抢锁失败(只有少部分节点给你写成功,要么抢锁的时间超过了设置的锁过期时间)
                        for_each_redis_registered(
                            redis =>
                            {
                                // 从所以节点执行移除锁的lua脚本
                                UnlockInstance(redis, resource, val);
                            }
                        );
                        return false;
                    }
                }
                catch (Exception)
                { return false; }
            });

            lockObject = innerLock;
            return successfull;
        }

        protected void for_each_redis_registered(Action<IConnectionMultiplexer> action)
        {
            foreach (var item in redisMasterDictionary)
            {
                action(item.Value);
            }
        }

        protected void for_each_redis_registered(Action<String> action)
        {
            foreach (var item in redisMasterDictionary)
            {
                action(item.Key);
            }
        }

        //补偿重试的源代码
        protected bool retry(int retryCount, TimeSpan retryDelay, Func<bool> action)
        {
            int maxRetryDelay = (int)retryDelay.TotalMilliseconds;
            Random rnd = new Random();
            int currentRetry = 0;
            while (currentRetry++ < retryCount)
            {
                if (action()) return true;
                // 微循环, 隔一段时间执行一次,隔一段时间执行一次
                Thread.Sleep(rnd.Next(maxRetryDelay));
            }
            return false;
        }

        public void Unlock(Lock lockObject)
        {
            for_each_redis_registered(redis =>
                {
                    UnlockInstance(redis, lockObject.Resource, lockObject.Value);
                });
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(this.GetType().FullName);

            sb.AppendLine("Registered Connections:");
            foreach(var item in redisMasterDictionary)
            {
                sb.AppendLine(item.Value.GetEndPoints().First().ToString());
            }

            return sb.ToString();
        }
    }
}
