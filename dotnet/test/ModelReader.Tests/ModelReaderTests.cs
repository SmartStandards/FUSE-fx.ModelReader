using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Data.ModelDescription;
using System.Linq;

namespace System.Data.Fuse.Tests {
  [TestClass]
  public class ModelReaderTests {

    [HasDependent(nameof(Person.Addresses), nameof(Address.Personalnummer), nameof(Address.Person))]
    [HasDependent("", nameof(WorkingAddress.PersonId), "", null, nameof(WorkingAddress))]
    public class Person {

      public int Nummer { get; set; }

      public string Name { get; set; } = string.Empty;

      public virtual ICollection<Address> Addresses { get; set; } = new ObservableCollection<Address>();

      public virtual AdditionalPersonData AdditionalPersonData { get; set; } = null!;
    }

    [HasPrincipal("", nameof(ContactData.Personnumber), "", null, nameof(Person))]
    public class ContactData {
      public int Personnumber { get; set; }
      public string Content { get; set; } = string.Empty;
    }

    public class WorkingAddress {
      public int PersonId { get; set; }
      public string Content { get; set; } = string.Empty;
    }

    [HasPrincipal(nameof(Address.Person), nameof(Address.Personalnummer), nameof(System.Data.Fuse.Tests.ModelReaderTests.Person.Addresses))]
    public class Address {
      public string Street { get; set; } = string.Empty;

      public int Personalnummer;

      public virtual Person Person { get; set; } = null!;
    }

    [HasPrincipal(nameof(AdditionalPersonData.Person), nameof(AdditionalPersonData.Personalnummer), nameof(System.Data.Fuse.Tests.ModelReaderTests.Person.AdditionalPersonData))]
    public class AdditionalPersonData {
      public string Name { get; set; } = string.Empty;

      public int Personalnummer;

      public virtual Person Person { get; set; } = null!;

    }

    [TestMethod]
    public void ModelReader_GetSchema_ReturnsRelationsCorrectly() {

      SchemaRoot schemaRoot = ModelReader.GetSchema(
        typeof(Person).Assembly,
        new string[] { nameof(Person), nameof(Address), nameof(AdditionalPersonData), nameof(ContactData), nameof(WorkingAddress) }
      );
      Assert.IsNotNull(schemaRoot);
      Assert.AreEqual(4, schemaRoot.Relations.Count());
    }
  }
}