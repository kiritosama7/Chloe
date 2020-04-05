﻿using Chloe.Core;
using Chloe.Core.Visitors;
using Chloe.DbExpressions;
using Chloe.Extensions;
using Chloe.Infrastructure;
using Chloe.InternalExtensions;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;

namespace Chloe
{
    public abstract partial class DbContext : IDbContext, IDisposable
    {
        static MethodInfo _saveMethod;
        static DbContext()
        {
            DbContext dbContext = null;
            Expression<Func<string>> e = () => dbContext.Save<string>("", null);
            MethodInfo method = (e.Body as MethodCallExpression).Method;
            _saveMethod = method;
        }


        protected DbCommandInfo Translate(DbExpression e)
        {
            IDbExpressionTranslator translator = this.DatabaseProvider.CreateDbExpressionTranslator();
            DbCommandInfo dbCommandInfo = translator.Translate(e);
            return dbCommandInfo;
        }
        protected async Task<int> ExecuteNonQuery(DbExpression e, bool @async)
        {
            DbCommandInfo dbCommandInfo = this.Translate(e);
            return await this.ExecuteNonQuery(dbCommandInfo, @async);
        }
        protected async Task<int> ExecuteNonQuery(DbCommandInfo dbCommandInfo, bool @async)
        {
            int rowsAffected;
            if (@async)
                rowsAffected = await this.Session.ExecuteNonQueryAsync(dbCommandInfo.CommandText, dbCommandInfo.GetParameters());
            else
                rowsAffected = this.Session.ExecuteNonQuery(dbCommandInfo.CommandText, dbCommandInfo.GetParameters());

            return rowsAffected;
        }
        protected async Task<int> ExecuteNonQuery(string cmdText, DbParam[] parameters, bool @async)
        {
            int rowsAffected;
            if (@async)
                rowsAffected = await this.Session.ExecuteNonQueryAsync(cmdText, parameters);
            else
                rowsAffected = this.Session.ExecuteNonQuery(cmdText, parameters);

            return rowsAffected;
        }
        protected async Task<object> ExecuteScalar(DbCommandInfo dbCommandInfo, bool @async)
        {
            object scalar;
            if (@async)
                scalar = await this.Session.ExecuteScalarAsync(dbCommandInfo.CommandText, dbCommandInfo.GetParameters());
            else
                scalar = this.Session.ExecuteScalar(dbCommandInfo.CommandText, dbCommandInfo.GetParameters());

            return scalar;
        }
        protected async Task<IDataReader> ExecuteReader(DbExpression e, bool @async)
        {
            DbCommandInfo dbCommandInfo = this.Translate(e);
            IDataReader dataReader = await this.ExecuteReader(dbCommandInfo, @async);
            return dataReader;
        }
        protected async Task<IDataReader> ExecuteReader(DbCommandInfo dbCommandInfo, bool @async)
        {
            IDataReader dataReader;
            if (@async)
                dataReader = await this.Session.ExecuteReaderAsync(dbCommandInfo.CommandText, dbCommandInfo.GetParameters());
            else
                dataReader = this.Session.ExecuteReader(dbCommandInfo.CommandText, dbCommandInfo.GetParameters());

            return dataReader;
        }

        static KeyValuePairList<JoinType, Expression> ResolveJoinInfo(LambdaExpression joinInfoExp)
        {
            /*
             * Usage:
             * var view = context.JoinQuery<User, City, Province, User, City>((user, city, province, user1, city1) => new object[] 
             * { 
             *     JoinType.LeftJoin, user.CityId == city.Id, 
             *     JoinType.RightJoin, city.ProvinceId == province.Id,
             *     JoinType.InnerJoin,user.Id==user1.Id,
             *     JoinType.FullJoin,city.Id==city1.Id
             * }).Select((user, city, province, user1, city1) => new { User = user, City = city, Province = province, User1 = user1, City1 = city1 });
             * 
             * To resolve join infomation:
             * JoinType.LeftJoin, user.CityId == city.Id               index of joinType is 0
             * JoinType.RightJoin, city.ProvinceId == province.Id      index of joinType is 2
             * JoinType.InnerJoin,user.Id==user1.Id                    index of joinType is 4
             * JoinType.FullJoin,city.Id==city1.Id                     index of joinType is 6
            */

            NewArrayExpression body = joinInfoExp.Body as NewArrayExpression;

            if (body == null)
            {
                throw new ArgumentException(string.Format("Invalid join infomation '{0}'. The correct usage is like: {1}", joinInfoExp, "context.JoinQuery<User, City>((user, city) => new object[] { JoinType.LeftJoin, user.CityId == city.Id })"));
            }

            KeyValuePairList<JoinType, Expression> ret = new KeyValuePairList<JoinType, Expression>();

            if ((joinInfoExp.Parameters.Count - 1) * 2 != body.Expressions.Count)
            {
                throw new ArgumentException(string.Format("Invalid join infomation '{0}'.", joinInfoExp));
            }

            for (int i = 0; i < joinInfoExp.Parameters.Count - 1; i++)
            {
                /*
                 * 0  0
                 * 1  2
                 * 2  4
                 * 3  6
                 * ...
                 */
                int indexOfJoinType = i * 2;

                Expression joinTypeExpression = body.Expressions[indexOfJoinType];
                object inputJoinType = ExpressionEvaluator.Evaluate(joinTypeExpression);
                if (inputJoinType == null || inputJoinType.GetType() != typeof(JoinType))
                    throw new ArgumentException(string.Format("Not support '{0}', please pass correct type of 'Chloe.JoinType'.", joinTypeExpression));

                /*
                 * The next expression of join type must be join condition.
                 */
                Expression joinCondition = body.Expressions[indexOfJoinType + 1].StripConvert();

                if (joinCondition.Type != PublicConstants.TypeOfBoolean)
                {
                    throw new ArgumentException(string.Format("Not support '{0}', please pass correct join condition.", joinCondition));
                }

                ParameterExpression[] parameters = joinInfoExp.Parameters.Take(i + 2).ToArray();

                List<Type> typeArguments = parameters.Select(a => a.Type).ToList();
                typeArguments.Add(PublicConstants.TypeOfBoolean);

                Type delegateType = Utils.GetFuncDelegateType(typeArguments.ToArray());
                LambdaExpression lambdaOfJoinCondition = Expression.Lambda(delegateType, joinCondition, parameters);

                ret.Add((JoinType)inputJoinType, lambdaOfJoinCondition);
            }

            return ret;
        }
        static MethodInfo GetSaveMethod(Type entityType)
        {
            MethodInfo method = _saveMethod.GetGenericMethodDefinition().MakeGenericMethod(entityType);
            return method;
        }
    }
}
