﻿using System.Collections.Generic;
using System.Linq.Expressions;
using Shaolinq.Persistence.Sql.Linq.Expressions;
using Platform;

namespace Shaolinq.Persistence.Sql.Linq.Optimizer
{
	public class RedundantSubqueryFinder
		: SqlExpressionVisitor
	{
		List<SqlSelectExpression> redundant;

		private RedundantSubqueryFinder()
		{
		}

		internal static List<SqlSelectExpression> Find(Expression source)
		{
			var gatherer = new RedundantSubqueryFinder();

			gatherer.Visit(source);

			return gatherer.redundant;
		}

		protected static bool IsInitialProjection(SqlSelectExpression select)
		{
			return select.From is SqlTableExpression;
		}

		internal static bool IsSimpleProjection(SqlSelectExpression select)
		{
			foreach (SqlColumnDeclaration decl in select.Columns)
			{
				var col = decl.Expression as SqlColumnExpression;

				if (col == null || decl.Name != col.Name)
				{
					return false;
				}
			}

			return true;
		}

		internal static bool IsNameMapProjection(SqlSelectExpression select)
		{
			if (select.From is SqlTableExpression)
			{
				return false;
			}

			var fromSelect = select.From as SqlSelectExpression;

			if (fromSelect == null || select.Columns.Count > fromSelect.Columns.Count)
			{
				return false;
			}

			var fromColumnNames = new HashSet<string>(fromSelect.Columns.Convert(x => x.Name));

			for (int i = 0; i < select.Columns.Count; i++)
			{
				var columnExpression = select.Columns[i].Expression as SqlColumnExpression;

				if (columnExpression == null || !fromColumnNames.Contains(columnExpression.Name))
				{
					return false;
				}
			}

			return true;
		}

		private static bool IsRedudantSubquery(SqlSelectExpression select)
		{
			return (IsSimpleProjection(select) || IsNameMapProjection(select))
				&& !select.Distinct
				&& select.Take == null
				&& select.Skip == null
				&& select.Where == null
				&& (select.OrderBy == null || select.OrderBy.Count == 0)
				&& (select.GroupBy == null || select.GroupBy.Count == 0);
		}

		protected override Expression VisitSelect(SqlSelectExpression select)
		{
			if (IsRedudantSubquery(select))
			{
				if (this.redundant == null)
				{
					this.redundant = new List<SqlSelectExpression>();
				}

				this.redundant.Add(select);
			}

			return select;
		}

		protected override Expression VisitSubquery(SqlSubqueryExpression subquery)
		{
			// Don't gather inside scalar & exists

			return subquery;
		}
	}
}