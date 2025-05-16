using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Data.ModelDescription;
using System.Linq;

namespace System.Data.Fuse.Tests {

  [TestClass]
  public class ModelReaderTests {

    [TestMethod]
    public void ModelReader_GetSchema_() {

      SchemaRoot schemaRoot = ModelReader.GetSchemaForDbContext<MockContext>();
      Assert.IsNotNull(schemaRoot);
    }

    public class MockContext : DbContext {

      public DbSet<AddressBook> Addresses { get; set; }
      public DbSet<WorkingAddress> WorkingAddresses { get; set; }
      public DbSet<Person> People { get; set; }
      public DbSet<ContactData> ContactData { get; set; }

    }

    //[TestMethod]
    //public void ModelReader_GetSchema_ReturnsRelationsCorrectly() {

    //  SchemaRoot schemaRoot = ModelReader.GetSchema(
    //    typeof(Person).Assembly,
    //    new string[] {
    //      nameof(Person),
    //      nameof(Address),
    //      nameof(AdditionalPersonData),
    //      nameof(ContactData),
    //      nameof(WorkingAddress)
    //    }
    //  );
    //  Assert.IsNotNull(schemaRoot);
    //  Assert.AreEqual(5, schemaRoot.Relations.Count());
    //  RelationSchema childrenRelation = schemaRoot.Relations.First(r => r.PrimaryNavigationName == nameof(Person.Children));
    //  EntitySchema personSchema = schemaRoot.Entities.First(e => e.Name == nameof(Person));
    //  Assert.IsNotNull(personSchema);
    //  Assert.IsNull(personSchema.Fields.FirstOrDefault((f) => f.Name == nameof(Person.Addresses)));
    //  Assert.IsNotNull(childrenRelation);
    //}

    /////////////////////////// MOCKS ///////////////////////////

    [HasDependent(nameof(Person.Addresses), nameof(Address.Personalnummer), nameof(Address.Person))]
    [HasDependent("", nameof(WorkingAddress.PersonId), "", null, nameof(WorkingAddress))]
    public class Person {

      public int Nummer { get; set; }

      public string Name { get; set; } = string.Empty;

      public int ParentId { get; set; }

      public virtual ICollection<Address> Addresses { get; set; } = new ObservableCollection<Address>();

      public virtual AdditionalPersonData AdditionalPersonData { get; set; } = null!;

      [Dependent]
      public virtual ICollection<Person> Children { get; set; } = new ObservableCollection<Person>();

      [Principal]
      public virtual Person Parent { get; set; } = null!;
    }

    public enum ContactType {
      Email,
      Phone,
      Fax
    }

    [HasPrincipal("", nameof(ContactData.Personnumber), "", null, nameof(Person))]
    public class ContactData {
      public int Personnumber { get; set; }
      public string Content { get; set; } = string.Empty;
      public ContactType contactType { get; set; } = ContactType.Email;
    }

    //[HasDependent(nameof(Addresses))]
    public class AddressBook {

      [Dependent]
      public ObservableCollection<Address> Addresses { get; set; } = new ObservableCollection<Address>();

    }

    public class WorkingAddress {
      public int PersonId { get; set; }
      public string Content { get; set; } = string.Empty;
      public ContactType ContactType { get; set; } = ContactType.Email;
    }

    [HasPrincipal(nameof(Address.Person), nameof(Address.Personalnummer), nameof(System.Data.Fuse.Tests.ModelReaderTests.Person.Addresses))]
    public class Address {
      public string Street { get; set; } = string.Empty;

      public int Personalnummer;

      [Principal]
      public virtual Person Person { get; set; } = null!;
    }

    [HasPrincipal(nameof(AdditionalPersonData.Person), nameof(AdditionalPersonData.Personalnummer), nameof(System.Data.Fuse.Tests.ModelReaderTests.Person.AdditionalPersonData))]
    public class AdditionalPersonData {
      public string Name { get; set; } = string.Empty;

      public int Personalnummer;

      public virtual Person Person { get; set; } = null!;

    }

    //      SchemaRoot schemaRoot = ModelReader.GetSchema(
    //        typeof(Person).Assembly,
    //        new string[] {
    //          nameof(Person),
    //          nameof(Address),
    //          nameof(AdditionalPersonData),
    //          nameof(ContactData),
    //          nameof(WorkingAddress)
    //        }
    //      );
    //      Assert.IsNotNull(schemaRoot);
    //      Assert.AreEqual(5, schemaRoot.Relations.Count());
    //      RelationSchema childrenRelation = schemaRoot.Relations.First(r => r.PrimaryNavigationName == nameof(Person.Children));
    //      EntitySchema personSchema = schemaRoot.Entities.First(e => e.Name == nameof(Person));
    //      Assert.IsNotNull(personSchema);
    //      Assert.IsNull(personSchema.Fields.FirstOrDefault((f) => f.Name == nameof(Person.Addresses)));
    //      Assert.IsNotNull(childrenRelation);
    //    }
  }

  
}