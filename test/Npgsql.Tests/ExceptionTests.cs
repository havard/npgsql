// project created on 30/11/2002 at 22:00
//
// Author:
//  David Rowley <dgrowley@gmail.com>
//     Francisco Figueiredo Jr. <fxjrlists@yahoo.com>
//
//    Copyright (C) 2002 The Npgsql Development Team
//    npgsql-general@gborg.postgresql.org
//    http://gborg.postgresql.org/project/npgsql/projdisplay.php
//
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
//
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA  02111-1307  USA
using System;
using System.Data;
using System.Web.UI.WebControls;

using Npgsql;
using NpgsqlTypes;

using NUnit.Framework;
using NUnit.Framework.Constraints;

namespace Npgsql.Tests
{
    [TestFixture]
    public class ExceptionTests : TestBase
    {
        public ExceptionTests(string backendVersion) : base(backendVersion) { }

        [Test, Description("Generates a basic server-side exception, checks that it's properly raised and populated")]
        public void Basic()
        {
            ExecuteNonQuery(@"
                 CREATE OR REPLACE FUNCTION emit_exception() RETURNS VOID AS
                    'BEGIN RAISE EXCEPTION ''testexception'' USING ERRCODE = ''12345''; END;'
                 LANGUAGE 'plpgsql';
            ");

            NpgsqlException ex = null;
            try
            {
                ExecuteNonQuery("SELECT emit_exception()");
                Assert.Fail("No exception was thrown");
            }
            catch (NpgsqlException e)
            {
                ex = e;
            }

            Assert.That(ex.MessageText, Is.EqualTo("testexception"));
            Assert.That(ex.Severity, Is.EqualTo(ErrorSeverity.Error));
            Assert.That(ex.Code, Is.EqualTo("12345"));
            Assert.That(ex.Position, Is.EqualTo(0));

            var data = ex.Data;
            Assert.That(data["Severity"], Is.EqualTo(ErrorSeverity.Error));
            Assert.That(data["Code"], Is.EqualTo("12345"));
            Assert.That(data.Contains("Position"), Is.False);

            Assert.That(ExecuteScalar("SELECT 1"), Is.EqualTo(1), "Connection in bad state after an exception");
        }

        [Test]
        public void ProblemSqlInsideException()
        {
            const string sql = "selec 1 as test";
            Assert.That(
                () => ExecuteScalar(sql),
                Throws.Exception.TypeOf<NpgsqlException>()
                  .With.Property("ErrorSql").EqualTo(sql)
            );
        }

        [Test]
        [MinPgVersion(9, 3, 0, "5 error fields haven't been added yet")]
        public void ExceptionFieldsArePopulated()
        {
            const string dropTable = @"DROP TABLE IF EXISTS public.uniqueviolation";
            const string createTable = @"CREATE TABLE public.uniqueviolation (id INT NOT NULL, CONSTRAINT uniqueviolation_pkey PRIMARY KEY (id))";
            const string insertStatement = @"INSERT INTO public.uniqueviolation (id) VALUES(1)";

            // In this case we'll test a simple unique violation, we're not too interested in testing more
            // cases than this as the same code is executed in all error situations.
            try
            {
                var command = new NpgsqlCommand(dropTable, Conn);
                command.ExecuteNonQuery();

                command = new NpgsqlCommand(createTable, Conn);
                command.ExecuteNonQuery();

                command = new NpgsqlCommand(insertStatement, Conn);
                command.ExecuteNonQuery();

                //Now cause the unique violation...
                command.ExecuteNonQuery();

            }
            catch (NpgsqlException ex)
            {
                Assert.That(ex.ColumnName, Is.Null, "ColumnName should not be populated for unique violations");
                Assert.That(ex.TableName, Is.EqualTo("uniqueviolation"));
                Assert.That(ex.SchemaName, Is.EqualTo("public"));
                Assert.That(ex.ConstraintName, Is.EqualTo("uniqueviolation_pkey"));
                Assert.That(ex.DataTypeName, Is.Null, "DataTypeName should not be populated for unique violations");
            }
        }

        [Test]
        [MinPgVersion(9, 3, 0, "5 error fields haven't been added yet")]
        public void ColumnNameExceptionFieldIsPopulated()
        {
            const string dropTable = @"DROP TABLE IF EXISTS public.notnullviolation";
            const string createTable = @"CREATE TABLE public.notnullviolation (id INT NOT NULL)";
            const string insertStatement = @"INSERT INTO public.notnullviolation (id) VALUES(NULL)";

            try
            {
                var command = new NpgsqlCommand(dropTable, Conn);
                command.ExecuteNonQuery();

                command = new NpgsqlCommand(createTable, Conn);
                command.ExecuteNonQuery();

                command = new NpgsqlCommand(insertStatement, Conn);
                //Cause the NOT NULL violation
                command.ExecuteNonQuery();

            }
            catch (NpgsqlException ex)
            {
                Assert.AreEqual("public", ex.SchemaName);
                Assert.AreEqual("notnullviolation", ex.TableName);
                Assert.AreEqual("id", ex.ColumnName);
            }
        }

        [Test]
        [MinPgVersion(9, 3, 0, "5 error fields haven't been added yet")]
        public void DataTypeNameExceptionFieldIsPopulated()
        {
            // On reading the source code for PostgreSQL9.3beta1, the only time that the
            // datatypename field is populated is when using domain types. So here we'll
            // create a domain that simply does not allow NULLs then try and cast NULL
            // to it.
            const string dropDomain = @"DROP DOMAIN IF EXISTS public.intnotnull";
            const string createDomain = @"CREATE DOMAIN public.intnotnull AS INT NOT NULL";
            const string castStatement = @"SELECT CAST(NULL AS public.intnotnull)";

            try
            {
                var command = new NpgsqlCommand(dropDomain, Conn);
                command.ExecuteNonQuery();

                command = new NpgsqlCommand(createDomain, Conn);
                command.ExecuteNonQuery();

                command = new NpgsqlCommand(castStatement, Conn);
                //Cause the NOT NULL violation
                command.ExecuteNonQuery();

            }
            catch (NpgsqlException ex)
            {
                Assert.AreEqual("public", ex.SchemaName);
                Assert.AreEqual("intnotnull", ex.DataTypeName);
            }
        }
    }
}
