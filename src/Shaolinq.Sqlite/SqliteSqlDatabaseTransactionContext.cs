﻿// Copyright (c) 2007-2013 Thong Nguyen (tumtumtum@gmail.com)

using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Transactions;
﻿using Shaolinq.Persistence;

namespace Shaolinq.Sqlite
{
	public class SqliteSqlDatabaseTransactionContext
		: SqlDatabaseTransactionContext
	{
		protected override char ParameterIndicatorChar
		{
			get
			{
				return '@';
			}
		}

		public SqliteSqlDatabaseTransactionContext(SystemDataBasedSqlDatabaseContext sqlDatabaseContext, DataAccessModel dataAccessModel, Transaction transaction)
			: base(sqlDatabaseContext, dataAccessModel, transaction)
		{
		}

		public virtual void RealDispose()
		{
			base.Dispose();
		}

		public override void Dispose()
		{
			if (!String.Equals(((SqliteSqlDatabaseContext)this.SqlDatabaseContext).FileName, ":memory:", StringComparison.InvariantCultureIgnoreCase))
			{
				base.Dispose();
			}
		}
	}
}
