﻿// Copyright (c) 2007-2013 Thong Nguyen (tumtumtum@gmail.com)

using System;
using System.Data.Common;
using System.Transactions;
﻿using Shaolinq.Persistence;
using Devart.Data.PostgreSql;
using Shaolinq.Postgres.Shared;

namespace Shaolinq.Postgres.DotConnect
{
    public class PostgresDotConnectSqlDatabaseContext
        : SystemDataBasedSqlDatabaseContext
    {
		public int Port { get; set; }
		public string Host { get; set; }
	    public string UserId { get; set; }
	    public string Password { get; set; }
	    public string DatabaseName { get; set; }
	    internal readonly string connectionString;
		internal readonly string databaselessConnectionString;

		public override string GetConnectionString()
        {
            return connectionString;
        }

		public static PostgresDotConnectSqlDatabaseContext Create(PostgresDotConnectSqlDatabaseContextInfo contextInfo, ConstraintDefaults constraintDefaults)
		{
			var sqlDialect = PostgresSharedSqlDialect.Default;
			var sqlDataTypeProvider = new PostgresSharedSqlDataTypeProvider(constraintDefaults, contextInfo.NativeUuids, contextInfo.DateTimeKindIfUnspecified);
			var sqlQueryFormatterManager = new DefaultSqlQueryFormatterManager(sqlDialect, sqlDataTypeProvider, (options, sqlDataTypeProviderArg, sqlDialectArg) => new PostgresSharedSqlQueryFormatter(options, sqlDataTypeProviderArg, sqlDialectArg, contextInfo.SchemaName));

			return new PostgresDotConnectSqlDatabaseContext(sqlDialect, sqlDataTypeProvider, sqlQueryFormatterManager, contextInfo);
		}

		protected PostgresDotConnectSqlDatabaseContext(SqlDialect sqlDialect, SqlDataTypeProvider sqlDataTypeProvider, SqlQueryFormatterManager sqlQueryFormatterManager, PostgresDotConnectSqlDatabaseContextInfo contextInfo)
			: base(sqlDialect, sqlDataTypeProvider, sqlQueryFormatterManager, contextInfo)
        {
            this.Host = contextInfo.ServerName;
            this.UserId = contextInfo.UserId;
            this.Password = contextInfo.Password;
			this.DatabaseName = contextInfo.DatabaseName;
			this.Port = contextInfo.Port;
			this.CommandTimeout = TimeSpan.FromSeconds(contextInfo.CommandTimeout);
			
            var sb = new PgSqlConnectionStringBuilder
			{
				Host = contextInfo.ServerName,
				UserId = contextInfo.UserId,
				Password = contextInfo.Password,
				Port = contextInfo.Port,
				Pooling = contextInfo.Pooling,
				Enlist = false,
				ConnectionTimeout = contextInfo.ConnectionTimeout,
				Charset = "UTF8",
				Unicode = true,
				MaxPoolSize = contextInfo.MaxPoolSize,
				DefaultCommandTimeout = contextInfo.CommandTimeout
			};

            databaselessConnectionString = sb.ConnectionString;

			sb.Database = contextInfo.DatabaseName;
            connectionString = sb.ConnectionString;
        }

        public override DatabaseTransactionContext CreateDatabaseTransactionContext(DataAccessModel dataAccessModel, Transaction transaction)
        {
            return new PostgresDotConnectSqlDatabaseTransactionContext(this, dataAccessModel, transaction);
        }

		public override DbProviderFactory CreateDbProviderFactory()
        {
            return PgSqlProviderFactory.Instance;
        }
		
	    public override DatabaseCreator CreateDatabaseCreator(DataAccessModel model)
	    {
		    return new PostgresDotConnectDatabaseCreator(this, model);
	    }

        public override IDisabledForeignKeyCheckContext AcquireDisabledForeignKeyCheckContext(DatabaseTransactionContext databaseTransactionContext)
        {
            return new DisabledForeignKeyCheckContext(databaseTransactionContext);	
        }

		public override void DropAllConnections()
		{
			PgSqlConnection.ClearAllPools();
		}

		public override Exception DecorateException(Exception exception, string relatedQuery)
		{
			var postgresException = exception as PgSqlException;

			if (postgresException == null)
			{
				return base.DecorateException(exception, relatedQuery);
			}

			if (postgresException.ErrorCode == "40001")
			{
				return new ConcurrencyException(exception, relatedQuery);
			}
			else if (postgresException.ErrorCode == "23505")
			{
				throw new UniqueKeyConstraintException(exception, relatedQuery);
			}

			return new DataAccessException(exception, relatedQuery);
		}
    }
}
