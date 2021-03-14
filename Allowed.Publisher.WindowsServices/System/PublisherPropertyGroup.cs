using System.Xml.Serialization;

namespace Allowed.Publisher.WindowsServices.Project
{
    [XmlRoot(ElementName = "PropertyGroup")]
	public class PublisherPropertyGroup
	{
		[XmlElement(ElementName = "TargetFramework")]
		public string TargetFramework { get; set; }
	}
}
