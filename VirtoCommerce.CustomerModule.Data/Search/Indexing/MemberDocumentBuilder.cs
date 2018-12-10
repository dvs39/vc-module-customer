using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VirtoCommerce.Domain.Catalog.Model;
using VirtoCommerce.Domain.Commerce.Model;
using VirtoCommerce.Domain.Customer.Model;
using VirtoCommerce.Domain.Customer.Services;
using VirtoCommerce.Domain.Search;
using VirtoCommerce.Platform.Core.Common;
using VirtoCommerce.Platform.Core.DynamicProperties;
using model = VirtoCommerce.Domain.Catalog.Model;

namespace VirtoCommerce.CustomerModule.Data.Search.Indexing
{    
    public class MemberDocumentBuilder : IIndexDocumentBuilder
    {
        private readonly IMemberService _memberService;

        public MemberDocumentBuilder(IMemberService memberService)
        {
            _memberService = memberService;
        }

        public virtual Task<IList<IndexDocument>> GetDocumentsAsync(IList<string> documentIds)
        {
            var members = GetMembers(documentIds);

            IList<IndexDocument> result = members.Select(CreateDocument).ToArray();
            return Task.FromResult(result);
        }


        protected virtual IList<Member> GetMembers(IList<string> documentIds)
        {
            return _memberService.GetByIds(documentIds.ToArray());
        }

        protected virtual IndexDocument CreateDocument(Member member)
        {
            var document = new IndexDocument(member.Id);

            document.AddFilterableValue("MemberType", member.MemberType);
            document.AddFilterableAndSearchableValue("Name", member.Name);
            document.AddFilterableAndSearchableValues("Emails", member.Emails);
            document.AddFilterableAndSearchableValues("Phones", member.Phones);
            document.AddFilterableValues("Groups", member.Groups);

            document.AddFilterableValue("CreatedDate", member.CreatedDate);
            document.AddFilterableValue("ModifiedDate", member.ModifiedDate ?? member.CreatedDate);

            if (member.Addresses?.Any() == true)
            {
                foreach (var address in member.Addresses)
                {
                    IndexAddress(document, address);
                }
            }

            if (member.Notes?.Any() == true)
            {
                foreach (var note in member.Notes)
                {
                    IndexNote(document, note);
                }
            }

            var contact = member as Contact;
            var employee = member as Employee;
            var organization = member as Organization;
            var vendor = member as Vendor;

            if (contact != null)
            {
                IndexContact(document, contact);

                IndexCustomProperties(document, contact.DynamicProperties, contact.DynamicProperties.Select(x => x.ValueType).ToList());
            }
            else if (employee != null)
            {
                IndexEmployee(document, employee);
            }
            else if (organization != null)
            {
                IndexOrganization(document, organization);
            }
            else if (vendor != null)
            {
                IndexVendor(document, vendor);
            }

            return document;
        }

        protected virtual void IndexAddress(IndexDocument document, Address address)
        {
            document.AddSearchableValue(address.AddressType.ToString());
            document.AddSearchableValue(address.Name);
            document.AddSearchableValue(address.Organization);
            document.AddSearchableValue(address.CountryCode);
            document.AddSearchableValue(address.CountryName);
            document.AddSearchableValue(address.City);
            document.AddSearchableValue(address.PostalCode);
            document.AddSearchableValue(address.Zip);
            document.AddSearchableValue(address.Line1);
            document.AddSearchableValue(address.Line2);
            document.AddSearchableValue(address.RegionId);
            document.AddSearchableValue(address.RegionName);
            document.AddSearchableValue(address.FirstName);
            document.AddSearchableValue(address.MiddleName);
            document.AddSearchableValue(address.LastName);
            document.AddSearchableValue(address.Phone);
            document.AddSearchableValue(address.Email);
        }

        protected virtual void IndexNote(IndexDocument document, Note note)
        {
            document.AddSearchableValue(note.Title);
            document.AddSearchableValue(note.Body);
        }

        protected virtual void IndexContact(IndexDocument document, Contact contact)
        {
            document.AddFilterableAndSearchableValue("Salutation", contact.Salutation);
            document.AddFilterableAndSearchableValue("FullName", contact.FullName);
            document.AddFilterableAndSearchableValue("FirstName", contact.FirstName);
            document.AddFilterableAndSearchableValue("MiddleName", contact.MiddleName);
            document.AddFilterableAndSearchableValue("LastName", contact.LastName);
            document.AddFilterableValue("BirthDate", contact.BirthDate);
            AddParentOrganizations(document, contact.Organizations);

            document.AddFilterableValue("TaxpayerId", contact.TaxPayerId);
            document.AddFilterableValue("PreferredDelivery", contact.PreferredDelivery);
            document.AddFilterableValue("PreferredCommunication", contact.PreferredCommunication);

            //Add emails from contact
            if (contact.Emails?.Any() == true)
            { 
                foreach (string email in contact.Emails)
                { 
                    document.AddSearchableValue(email);
                }
            }
        }

        protected virtual void IndexEmployee(IndexDocument document, Employee employee)
        {
            document.AddFilterableAndSearchableValue("Salutation", employee.Salutation);
            document.AddFilterableAndSearchableValue("FullName", employee.FullName);
            document.AddFilterableAndSearchableValue("FirstName", employee.FirstName);
            document.AddFilterableAndSearchableValue("MiddleName", employee.MiddleName);
            document.AddFilterableAndSearchableValue("LastName", employee.LastName);
            document.AddFilterableValue("BirthDate", employee.BirthDate);
            AddParentOrganizations(document, employee.Organizations);

            document.AddFilterableValue("EmployeeType", employee.EmployeeType);
            document.AddFilterableValue("IsActive", employee.IsActive);
        }

        protected virtual void IndexOrganization(IndexDocument document, Organization organization)
        {
            document.AddSearchableValue(organization.Description);
            document.AddFilterableValue("BusinessCategory", organization.BusinessCategory);
            document.AddFilterableValue("OwnerId", organization.OwnerId);
            AddParentOrganizations(document, new[] { organization.ParentId });
        }

        protected virtual void IndexVendor(IndexDocument document, Vendor vendor)
        {
            document.AddSearchableValue(vendor.Description);
            document.AddFilterableValue("GroupName", vendor.GroupName);
        }

        protected virtual void AddParentOrganizations(IndexDocument document, ICollection<string> values)
        {
            var nonEmptyValues = values?.Where(v => !string.IsNullOrEmpty(v)).ToArray();

            document.AddFilterableValues("ParentOrganizations", nonEmptyValues);
            document.AddFilterableValue("HasParentOrganizations", nonEmptyValues?.Any() ?? false);
        }

        protected virtual void IndexCustomProperties(IndexDocument document, ICollection<DynamicObjectProperty> properties, ICollection<DynamicPropertyValueType> contentPropertyTypes)
        {
            foreach (var property in properties)
            {
                var propertyName = property.Name?.ToLowerInvariant();
                if (!string.IsNullOrEmpty(propertyName))
                {
                    var isCollection = property?.IsDictionary == true || property?.IsArray == true;
                    IList<object> values = new List<object>();

                    switch (property.ValueType)
                    {
                        case DynamicPropertyValueType.Boolean:
                        case DynamicPropertyValueType.DateTime:
                        case DynamicPropertyValueType.Integer:
                        case DynamicPropertyValueType.Decimal:
                            if (property?.IsArray == true)
                            {
                                values.AddRange((property.Values as IEnumerable<DynamicPropertyObjectValue>).Select(x => x.Value));
                                document.Add(new IndexDocumentField(propertyName, values) { IsRetrievable = true, IsFilterable = true, IsCollection = isCollection });
                            }
                            else
                            {
                                document.Add(new IndexDocumentField(propertyName, property.Values.FirstOrDefault()?.Value) { IsRetrievable = true, IsFilterable = true, IsCollection = isCollection });
                            }

                            break;
                        case DynamicPropertyValueType.LongText:
                            document.Add(new IndexDocumentField(propertyName, property.Values.FirstOrDefault().Value.ToString().ToLowerInvariant()) { IsRetrievable = true, IsSearchable = true, IsCollection = isCollection });
                            break;
                        case DynamicPropertyValueType.ShortText:
                            if (property?.IsDictionary == true)
                            {
                                //add all locales in dictionary to searchIndex
                                values.AddRange((property.Values as IEnumerable<DynamicPropertyObjectValue>).Select(x => x.Value)
                                                                        .Cast<DynamicPropertyDictionaryItem>()
                                                                        .Select(x => x.Name));
                            }

                            else if (property?.IsArray == true)
                            {
                                values.AddRange((property.Values as IEnumerable<DynamicPropertyObjectValue>).Select(x => x.Value));
                            }

                            document.Add(new IndexDocumentField(propertyName, values) { IsRetrievable = true, IsFilterable = true, IsCollection = isCollection });
                            break;
                    }
                }
            }
        }
    }
}
