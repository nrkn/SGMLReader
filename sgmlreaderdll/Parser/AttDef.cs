using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using SgmlReaderDll.Parser.Enums;

namespace SgmlReaderDll.Parser {
  /// <summary>
  /// An attribute definition in a DTD.
  /// </summary>
  public class AttDef
  {
    private AttributeType _type;

    /// <summary>
    /// Initialises a new instance of the <see cref="AttDef"/> class.
    /// </summary>
    /// <param name="name">The name of the attribute.</param>
    public AttDef(string name)
    {
      Name = name;
    }

    /// <summary>
    /// The name of the attribute declared by this attribute definition.
    /// </summary>
    public string Name { get; private set; }

    /// <summary>
    /// Gets of sets the default value of the attribute.
    /// </summary>
    public string Default { get; set; }

    /// <summary>
    /// The constraints on the attribute's presence on an element.
    /// </summary>
    public AttributePresence AttributePresence { get; private set; }

    /// <summary>
    /// Gets or sets the possible enumerated values for the attribute.
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1819", Justification = "Changing this would break backwards compatibility with previous code using this library.")]
    public string[] EnumValues { get; private set; }

    /// <summary>
    /// Sets the attribute definition to have an enumerated value.
    /// </summary>
    /// <param name="enumValues">The possible values in the enumeration.</param>
    /// <param name="type">The type to set the attribute to.</param>
    /// <exception cref="ArgumentException">If the type parameter is not either <see cref="AttributeType.ENUMERATION"/> or <see cref="AttributeType.NOTATION"/>.</exception>
    public void SetEnumeratedType(string[] enumValues, AttributeType type)
    {
      if (type != AttributeType.ENUMERATION && type != AttributeType.NOTATION)
        throw new ArgumentException(string.Format(CultureInfo.CurrentUICulture, "AttributeType {0} is not valid for an attribute definition with an enumerated value.", type));

      EnumValues = enumValues;
      _type = type;
    }

    /// <summary>
    /// The <see cref="AttributeType"/> of the attribute declaration.
    /// </summary>
    public AttributeType Type 
    {
      get
      {
        return _type;
      }
    }

    /// <summary>
    /// Sets the type of the attribute definition.
    /// </summary>
    /// <param name="type">The string representation of the attribute type, corresponding to the values in the <see cref="AttributeType"/> enumeration.</param>
    public void SetType(string type)
    {
      if( !Enum.TryParse( type, out _type )) {
        throw new SgmlParseException( string.Format( CultureInfo.CurrentUICulture, "Attribute type '{0}' is not supported", type ) );
      }
    }

    /// <summary>
    /// Sets the attribute presence declaration.
    /// </summary>
    /// <param name="token">The string representation of the attribute presence, corresponding to one of the values in the <see cref="AttributePresence"/> enumeration.</param>
    /// <returns>true if the attribute presence implies the element has a default value.</returns>
    public bool SetPresence(string token)
    {      
      AttributePresence attributePresence;

      if( !Enum.TryParse( token, true, out attributePresence ) ) {
        throw new SgmlParseException( string.Format( CultureInfo.CurrentUICulture, "Attribute value '{0}' not supported", token ) );
      }

      AttributePresence = attributePresence;

      return AttributePresence == AttributePresence.Fixed;
    }
  }
}