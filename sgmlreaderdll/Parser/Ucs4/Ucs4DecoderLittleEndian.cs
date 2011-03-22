using System;

namespace SgmlReaderDll.Parser.Ucs4 {
  internal class Ucs4DecoderLittleEndian : Ucs4Decoder {
    internal override uint GetCode( int i, byte[] bytes ) {
      return (UInt32)(((bytes[i]) << 24) | (bytes[i + 1] << 16) | (bytes[i + 2] << 8) | (bytes[i + 3]));
    }
  }
}