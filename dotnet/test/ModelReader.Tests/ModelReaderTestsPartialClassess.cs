using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Data.ModelDescription;
using System.Linq;

namespace System.Data.Fuse.Tests {

  [TestClass]
  public class ModelReaderTestsPartialClassess {

    [TestMethod]
    public void ModelReader_GetSchema_() {

      SchemaRoot schemaRoot = ModelReader.GetSchemaForDbContext<MockContext>();
      string? primaryPersonIndex = schemaRoot.Entities.First(e => e.Name == nameof(Person))?.PrimaryKeyIndexName;
      Assert.IsNotNull(primaryPersonIndex);
      Assert.AreEqual("Nummer", primaryPersonIndex);
    }

    public class MockContext : DbContext {

      public DbSet<Person> People { get; set; }

    }



    /////////////////////////// MOCKS ///////////////////////////

    [PrimaryIdentity(nameof(Nummer))]
    [PropertyGroup(nameof(Nummer), nameof(Nummer))]
    public partial class Person {
    }

   
    public partial class Person {

      public int Nummer { get; set; }

    }
  }

}