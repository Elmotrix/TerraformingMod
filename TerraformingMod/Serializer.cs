using System.Xml.Serialization;
using Assets.Scripts.Atmospherics;
using Assets.Scripts.Objects;

namespace TerraformingMod
{
    [XmlInclude(typeof(ThingSaveData))]
    [XmlRoot("TerraformingAtmosphere")]
    public class TerraformingAtmosphere
    {
        [XmlElement]
        public GasMixSaveData GasMix = null;
    }
}
