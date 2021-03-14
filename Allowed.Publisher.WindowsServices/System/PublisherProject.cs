using Allowed.Publisher.WindowsServices.Project;
using System.Xml.Serialization;

namespace Allowed.Publisher.WindowsServices.System
{
    [XmlRoot(ElementName = "Project")]
	public class PublisherProject
	{
		[XmlElement(ElementName = "PropertyGroup")]
		public PublisherPropertyGroup PropertyGroup { get; set; }
	}
}
