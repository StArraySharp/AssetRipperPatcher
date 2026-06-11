using System;
using System.Globalization;
using System.IO;

namespace AssetRipper.Export.Modules.Shaders.IO;

public sealed class InvariantStringWriter : StringWriter
{
	public override IFormatProvider FormatProvider => CultureInfo.InvariantCulture;
}
