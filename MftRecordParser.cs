using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Buffers.Binary;

namespace RecoveryLab
{
    internal class MftRecordParser
    {

        public static byte[] ApplyUsaFixups(byte[] record, int bytesPerSector)
        {

            if (record == null)
            {

                throw new ArgumentNullException(nameof(record));

            }

            if (bytesPerSector < 2 || record.Length % bytesPerSector != 0)
            {

                throw new InvalidDataException("Invalid sector or record size!");

            }

            if (record.Length < 8 || record[0] != (byte)'F' || record[1] != (byte)'I' || record[2] != (byte)'L' || record[3] != (byte)'E')
            {

                throw new InvalidDataException("Invalid MFT record signature");

            }

            ushort usaOffset = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(4,2));

            ushort usaCount = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(6, 2));

            int numberOfSectors = record.Length / bytesPerSector;

            if (usaCount != numberOfSectors + 1)
            {

                throw new InvalidDataException("Invalid USA count");

            }

            if (usaOffset > record.Length || usaCount * 2 > record.Length - usaOffset)
            {

                throw new InvalidDataException("USA is outside the record");

            }

            ushort updateSequenceNumber = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(usaOffset, 2));

            byte[] fixedRecord = (byte[])record.Clone();

            for (int i = 0; i < numberOfSectors; i++)
            {

                int trailerOffset = ((i + 1) * bytesPerSector) - 2;

                ushort trailerValue = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(trailerOffset, 2));

                if (trailerValue != updateSequenceNumber)
                {

                    throw new InvalidDataException($"USA validation failed at sector {i}.");

                }

                int replacementOffset = usaOffset + ((i + 1) * 2);

                ushort replacementValue = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(replacementOffset, 2));

                BinaryPrimitives.WriteUInt16LittleEndian(fixedRecord.AsSpan(trailerOffset, 2), replacementValue);

            }

            return fixedRecord;

        }
//-----------------------------------------------------------------------------------------------------------------------------
        public static void PrintAttributes(byte[] record) => ReadAttributes(record, Console.Out);

        internal static IReadOnlyList<NtfsAttribute> ReadAttributes(byte[] record) =>
            ReadAttributes(record, TextWriter.Null);

        private static IReadOnlyList<NtfsAttribute> ReadAttributes(byte[] record, TextWriter output)
        {

            if (record == null)
            {

                throw new ArgumentNullException(nameof(record));

            }

 
            ushort firstAttributeOffset = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(20, 2));

            uint usedSize = BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(24, 4));

            if (usedSize > (uint)record.Length || usedSize < firstAttributeOffset || firstAttributeOffset < 48)
            {
                
                throw new InvalidDataException("Invalid MFT record boundaries.");

            }

            int usedEnd = (int)usedSize;
            int attributeOffset = firstAttributeOffset;

            output.WriteLine();
            output.WriteLine("MFT ATTRIBUTES:");
            List<NtfsAttribute> attributes = new();

            while (attributeOffset <= usedEnd - 4)
            {

                uint attributeType = BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(attributeOffset, 4));

                if (attributeType == 0xFFFFFFFF)
                {

                    output.WriteLine("End of attributes reached.");

                    return attributes;

                }

                if (attributeOffset > usedEnd - 16)
                {

                    throw new InvalidDataException("Incomplete attribute header.");

                }


                uint attributeLength = BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(attributeOffset + 4, 4));

                byte nonResidentFlag = record[attributeOffset + 8];

                byte nameLength = record[attributeOffset + 9];

                ushort nameOffset = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(attributeOffset + 10, 2));

                ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(attributeOffset + 12, 2));

                ushort attributeId = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(attributeOffset + 14, 2));


                if (attributeLength < 16 || attributeLength > (uint)(usedEnd - attributeOffset))
                {

                    throw new InvalidDataException("Invalid attribute length.");

                }

                if (nonResidentFlag > 1)
                {

                    throw new InvalidDataException("Invalid residency flag.");

                }

                int length = (int)attributeLength;

                string attributeName = "(unnamed)";

                if (nameLength > 0)
                {

                    int nameByteLength = nameLength * 2;

                    if (nameOffset < 16 || nameOffset > length || nameByteLength > length - nameOffset)
                    {

                        throw new InvalidDataException("Invalid attribute name.");

                    }

                    attributeName = Encoding.Unicode.GetString(
                        record,
                        attributeOffset + nameOffset,
                        nameByteLength);
                }


                string typeName = attributeType switch
                {
                    0x10 => "$STANDARD_INFORMATION",
                    0x20 => "$ATTRIBUTE_LIST",
                    0x30 => "$FILE_NAME",
                    0x40 => "$OBJECT_ID",
                    0x50 => "$SECURITY_DESCRIPTOR",
                    0x60 => "$VOLUME_NAME",
                    0x70 => "$VOLUME_INFORMATION",
                    0x80 => "$DATA",
                    0x90 => "$INDEX_ROOT",
                    0xA0 => "$INDEX_ALLOCATION",
                    0xB0 => "$BITMAP",
                    0xC0 => "$REPARSE_POINT",
                    0xD0 => "$EA_INFORMATION",
                    0xE0 => "$EA",
                    0xF0 => "$PROPERTY_SET",
                    0x100 => "$LOGGED_UTILITY_STREAM",
                    _ => "Unknown attribute"
                };

                output.WriteLine();
                output.WriteLine($"Attribute: {typeName}");
                output.WriteLine($"Type ID: 0x{attributeType:X}");
                output.WriteLine($"Length: {attributeLength} bytes");
                output.WriteLine($"Resident: {nonResidentFlag == 0}");
                output.WriteLine($"Name: {attributeName}");
                output.WriteLine($"Flags: 0x{flags:X4}");
                output.WriteLine($"Attribute ID: {attributeId}");

                attributes.Add(new NtfsAttribute(attributeType, typeName, attributeLength,
                    nonResidentFlag == 0, nameLength == 0 ? null : attributeName, flags, attributeId));

                attributeOffset += length;
            }

            throw new InvalidDataException("Attribute end marker was not found within the used record size.");
        }
//------------------------------------------------------------------------------------------------------------------------------
        public static void ParseStandardInformation(byte[] record)
        {
            if (ReadStandardInformation(record, Console.Out) is null)
                throw new InvalidDataException("$STANDARD_INFORMATION attribute was not found.");
        }

        internal static NtfsStandardInformation? ReadStandardInformation(byte[] record) =>
            ReadStandardInformation(record, TextWriter.Null);

        private static NtfsStandardInformation? ReadStandardInformation(byte[] record, TextWriter output)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }

            ushort firstAttributeOffset = BinaryPrimitives.ReadUInt16LittleEndian(
                    record.AsSpan(20, 2));

            uint usedSize = BinaryPrimitives.ReadUInt32LittleEndian(
                    record.AsSpan(24, 4));

            if (usedSize > (uint)record.Length || usedSize < firstAttributeOffset)
            {

                throw new InvalidDataException("Invalid record boundaries.");

            }

            int usedEnd = (int)usedSize;
            int attributeOffset = firstAttributeOffset;


            while (attributeOffset <= usedEnd - 4)
            {

                uint attributeType = BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(attributeOffset, 4));

                if (attributeType == 0xFFFFFFFF)
                {

                    break;

                }

                if (attributeOffset > usedEnd - 16)
                {

                    throw new InvalidDataException("Incomplete attribute header.");

                }

                uint attributeLength = BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(attributeOffset + 4, 4));

                byte nonResidentFlag = record[attributeOffset + 8];

                if (attributeLength < 16 || attributeLength > (uint)(usedEnd - attributeOffset))
                {

                    throw new InvalidDataException("Invalid attribute length.");

                }

                if (attributeType == 0x10)
                {
                    
                    output.WriteLine();
                    output.WriteLine("$STANDARD_INFORMATION:");


                    if (nonResidentFlag != 0)
                    {

                        throw new InvalidDataException("$STANDARD_INFORMATION is not resident.");

                    }

                    if (attributeLength < 24)
                    {

                        throw new InvalidDataException("Resident attribute header is incomplete.");

                    }


                    uint valueLength = BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(attributeOffset + 16, 4));

                    ushort valueOffset = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(attributeOffset + 20, 2));

                    if (valueOffset < 24 || valueOffset > attributeLength || valueLength > attributeLength - valueOffset)
                    {

                        throw new InvalidDataException("Invalid resident attribute value boundaries.");

                    }

                    if (valueLength < 36)
                    {

                        throw new InvalidDataException("$STANDARD_INFORMATION value is too short.");

                    }


                    int valueStart = attributeOffset + valueOffset;


                    ulong creationTime = BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(valueStart, 8));

                    ulong modificationTime = BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(valueStart + 8, 8));

                    ulong mftModificationTime = BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(valueStart + 16, 8));

                    ulong accessTime = BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(valueStart + 24, 8));

                    uint fileAttributes = BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(valueStart + 32, 4));


                    output.WriteLine($"Value length: {valueLength} bytes");

                    output.WriteLine($"Creation time: {FormatNtfsTime(creationTime)}");

                    output.WriteLine($"Modification time: {FormatNtfsTime(modificationTime)}");

                    output.WriteLine($"MFT modification time: {FormatNtfsTime(mftModificationTime)}");

                    output.WriteLine($"Last access time: {FormatNtfsTime(accessTime)}");

                    output.WriteLine($"File attributes: 0x{fileAttributes:X8}");

                    return new NtfsStandardInformation(ReadTimes(creationTime, modificationTime,
                        mftModificationTime, accessTime), fileAttributes);

                }

                attributeOffset += (int)attributeLength;

            }

            return null;

        }

        private static NtfsTimes ReadTimes(ulong created, ulong modified, ulong mftModified, ulong accessed) =>
            new(ReadNtfsTime(created), ReadNtfsTime(modified), ReadNtfsTime(mftModified), ReadNtfsTime(accessed));

        private static string FormatNtfsTime(ulong fileTime) =>
            ReadNtfsTime(fileTime) is DateTime time ? $"{time:yyyy-MM-dd HH:mm:ss.fffffff} UTC" : "Invalid FILETIME";

        private static DateTime? ReadNtfsTime(ulong fileTime)
        {
            if (fileTime > long.MaxValue)
                return null;

            try
            {
                return DateTime.FromFileTimeUtc((long)fileTime);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        public static void ParseFileName(byte[] record)
        {
            if (ReadFileNames(record, Console.Out).Count == 0)
                throw new InvalidDataException("$FILE_NAME attribute was not found.");
        }

        internal static IReadOnlyList<NtfsFileName> ReadFileNames(byte[] record) =>
            ReadFileNames(record, TextWriter.Null);

        private static IReadOnlyList<NtfsFileName> ReadFileNames(byte[] record, TextWriter output)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }

            ushort firstAttributeOffset = BinaryPrimitives.ReadUInt16LittleEndian(
                    record.AsSpan(20, 2));

            uint usedSize = BinaryPrimitives.ReadUInt32LittleEndian(
                    record.AsSpan(24, 4));

            if (usedSize > (uint)record.Length || usedSize < firstAttributeOffset)
            {

                throw new InvalidDataException("Invalid record boundaries.");

            }

            int usedEnd = (int)usedSize;
            int attributeOffset = firstAttributeOffset;
            List<NtfsFileName> names = new();

            output.WriteLine();
            output.WriteLine("$FILE_NAME INFORMATION:");

            while (attributeOffset <= usedEnd - 4)
            {

                uint attributeType = BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(attributeOffset, 4));


                if (attributeType == 0xFFFFFFFF)
                {

                    break;

                }

                if (attributeOffset > usedEnd - 16)
                {

                    throw new InvalidDataException("Incomplete attribute header.");

                }

                uint attributeLength = BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(attributeOffset + 4, 4));

                byte nonResidentFlag = record[attributeOffset + 8];

                if (attributeLength < 16 || attributeLength > (uint)(usedEnd - attributeOffset))
                {

                    throw new InvalidDataException(
                        "Invalid attribute length.");

                }


                if (attributeType == 0x30)
                {

                    if (nonResidentFlag != 0)
                    {

                        throw new InvalidDataException("$FILE_NAME must be resident.");

                    }

                    if (attributeLength < 24)
                    {

                        throw new InvalidDataException("Incomplete resident attribute header.");

                    }


                    uint valueLength = BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(attributeOffset + 16, 4));

                    ushort valueOffset = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(attributeOffset + 20, 2));

                    if (valueOffset < 24 || valueOffset > attributeLength || valueLength > attributeLength - valueOffset)
                    {

                        throw new InvalidDataException("Invalid $FILE_NAME value boundaries.");
                    }


                    if (valueLength < 66)
                    {

                        throw new InvalidDataException("$FILE_NAME value is too short.");

                    }

                    int valueStart = attributeOffset + valueOffset;


                    ulong parentReference = BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(valueStart, 8));

                    ulong parentRecordNumber = parentReference & 0x0000FFFFFFFFFFFFUL;

                    ushort parentSequenceNumber = (ushort)(parentReference >> 48);

                    ulong creationTime = BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(valueStart + 8, 8));

                    ulong modificationTime = BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(valueStart + 16, 8));

                    ulong mftModificationTime = BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(valueStart + 24, 8));

                    ulong accessTime = BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(valueStart + 32, 8));

                    ulong allocatedSize = BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(valueStart + 40, 8));

                    ulong realSize = BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(valueStart + 48, 8));

                    uint fileAttributes = BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(valueStart + 56, 4));

                    uint reparsePointOrEa = BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(valueStart + 60, 4));

                    byte fileNameLength = record[valueStart + 64];

                    byte nameNamespace = record[valueStart + 65];

                    int nameByteLength = fileNameLength * 2;

                    if (nameByteLength > (int)valueLength - 66)
                    {

                        throw new InvalidDataException("Filename extends beyond the attribute value.");

                    }


                    string fileName = Encoding.Unicode.GetString(record, valueStart + 66, nameByteLength);

                    string namespaceName = nameNamespace switch
                    {
                        0 => "POSIX",
                        1 => "Win32",
                        2 => "DOS",
                        3 => "Win32 & DOS",
                        _ => "Unknown"
                    };

                    names.Add(new NtfsFileName(fileName, nameNamespace, namespaceName, parentRecordNumber,
                        parentSequenceNumber, ReadTimes(creationTime, modificationTime, mftModificationTime, accessTime),
                        allocatedSize, realSize, fileAttributes, reparsePointOrEa));

                    output.WriteLine();
                    output.WriteLine($"FILE_NAME attribute #{names.Count}");

                    output.WriteLine($"Filename: {fileName}");
                    output.WriteLine($"Namespace: {namespaceName}");

                    output.WriteLine($"Parent record number: {parentRecordNumber}");

                    output.WriteLine($"Parent sequence number: {parentSequenceNumber}");

                    output.WriteLine($"Creation time: {FormatNtfsTime(creationTime)}");

                    output.WriteLine($"Modification time: {FormatNtfsTime(modificationTime)}");

                    output.WriteLine($"MFT modification time: {FormatNtfsTime(mftModificationTime)}");

                    output.WriteLine($"Last access time: {FormatNtfsTime(accessTime)}");

                    output.WriteLine($"Allocated size: {allocatedSize} bytes");
                    output.WriteLine($"Real size: {realSize} bytes");

                    output.WriteLine($"File attributes: 0x{fileAttributes:X8}");

                    output.WriteLine($"Reparse point / EA information: 0x{reparsePointOrEa:X8}");
                }


                attributeOffset += (int)attributeLength;
            
            }

            
            return names;
        }
//-----------------------------------------------------------------------------------------------------------------------------
        public static void ParseDataAttribute(byte[] record)
        {
            if (ReadDataStreams(record, Console.Out).Count == 0)
                throw new InvalidDataException("$DATA attribute was not found.");
        }

        internal static IReadOnlyList<NtfsDataStream> ReadDataStreams(byte[] record) =>
            ReadDataStreams(record, TextWriter.Null);

        private static IReadOnlyList<NtfsDataStream> ReadDataStreams(byte[] record, TextWriter output)
        {

            if (record == null)
            {

                throw new ArgumentNullException(nameof(record));

            }


            ushort firstAttributeOffset = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(20, 2));

            uint usedSize = BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(24, 4));

            if (usedSize > (uint)record.Length || usedSize < firstAttributeOffset)
            {

                throw new InvalidDataException("Invalid record boundaries.");

            }

            int usedEnd = (int)usedSize;

            int attributeOffset = firstAttributeOffset;

            List<NtfsDataStream> streams = new();

            output.WriteLine();
            output.WriteLine("$DATA ATTRIBUTE INFORMATION:");


            while (attributeOffset <= usedEnd - 4)
            {

                uint attributeType = BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(attributeOffset, 4));


                if (attributeType == 0xFFFFFFFF)
                {

                    break;

                }

                if (attributeOffset > usedEnd - 16)
                {

                    throw new InvalidDataException("Incomplete attribute header.");

                }

                uint attributeLength = BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(attributeOffset + 4, 4));

                byte nonResidentFlag = record[attributeOffset + 8];

                byte nameLength = record[attributeOffset + 9];

                ushort nameOffset = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(attributeOffset + 10, 2));

                ushort attributeFlags = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(attributeOffset + 12, 2));

                ushort attributeId = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(attributeOffset + 14, 2));


                
                if (attributeLength < 16 || attributeLength > (uint)(usedEnd - attributeOffset))
                {

                    throw new InvalidDataException("Invalid attribute length.");

                }

                if (nonResidentFlag > 1)
                {

                    throw new InvalidDataException("Invalid residency flag.");

                }


                int length = (int)attributeLength;

                if (attributeType == 0x80)
                {

                    string attributeName = "(unnamed)";

                    if (nameLength > 0)
                    {

                        int nameByteLength = nameLength * 2;

                        if (nameOffset < 16 || nameOffset > length || nameByteLength > length - nameOffset)
                        {

                            throw new InvalidDataException("Invalid attribute name boundaries.");

                        }

                        attributeName = Encoding.Unicode.GetString(record, attributeOffset + nameOffset, nameByteLength);
                    }

                    output.WriteLine();
                    output.WriteLine($"DATA attribute #{streams.Count + 1}");
                    output.WriteLine($"Attribute ID: {attributeId}");
                    output.WriteLine($"Attribute name: {attributeName}");
                    output.WriteLine($"Total attribute length: {attributeLength} bytes");
                    output.WriteLine($"Attribute flags: 0x{attributeFlags:X4}");


                    if (nonResidentFlag == 0)
                    {
                        output.WriteLine("Residency: Resident");

                        output.WriteLine("Contents are stored inside the MFT record.");

                        if (attributeLength < 24)
                        {

                            throw new InvalidDataException(
                                "Incomplete resident DATA header.");

                        }


                        uint valueLength = BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(attributeOffset + 16, 4));

                        ushort valueOffset = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(attributeOffset + 20, 2));


                        if (valueOffset < 24 || valueOffset > attributeLength || valueLength > attributeLength - valueOffset)
                        {

                            throw new InvalidDataException("Invalid resident DATA boundaries.");

                        }

                        output.WriteLine($"Resident value length: {valueLength} bytes");

                        output.WriteLine($"Resident value offset: {valueOffset}");

                        streams.Add(new NtfsDataStream(attributeId, nameLength == 0 ? null : attributeName,
                            attributeFlags, true, valueLength, null, null, null, null, null, null, []));

                        if (output != TextWriter.Null)
                        {
                            int valueStart = attributeOffset + valueOffset;

                            output.WriteLine("Resident data bytes:");

                            int residentValueLength = (int)valueLength;

                            for (int i = 0; i < residentValueLength; i++)
                            {

                                output.Write($"{record[valueStart + i]:X2} ");

                                if ((i + 1) % 16 == 0)
                                {

                                    output.WriteLine();

                                }

                            }

                            output.WriteLine();
                        }

                        attributeOffset += length;

                        continue;

                    }

                    if (attributeLength < 64)
                    {

                        throw new InvalidDataException("Non-resident DATA header is incomplete.");

                    }


                    ulong startingVCN = BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(attributeOffset + 16, 8));

                    ulong endingVCN = BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(attributeOffset + 24, 8));

                    ushort mappingPairsOffset = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(attributeOffset + 32, 2));

                    ushort compressionUnit = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(attributeOffset + 34, 2));

                    ulong allocatedSize = BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(attributeOffset + 40, 8));

                    ulong dataSize = BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(attributeOffset + 48, 8));

                    ulong initializedSize = BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(attributeOffset + 56, 8));


                    if (mappingPairsOffset < 64 || mappingPairsOffset >= attributeLength)
                    {

                        throw new InvalidDataException("Invalid mapping pairs offset.");

                    }

                    output.WriteLine("Residency: Non-resident");
                    output.WriteLine($"Starting VCN: {startingVCN}");
                    output.WriteLine($"Ending VCN: {endingVCN}");
                    output.WriteLine($"Mapping pairs offset: {mappingPairsOffset}");

                    output.WriteLine($"Compression unit: {compressionUnit}");


                    if (startingVCN == 0)
                    {

                        output.WriteLine($"Allocated size: {allocatedSize} bytes");

                        output.WriteLine($"Real data size: {dataSize} bytes");

                        output.WriteLine($"Initialized size: {initializedSize} bytes");

                    }
                    else
                    {

                        output.WriteLine("This is an additional extent; " + "the complete stream sizes are not read here.");

                    }


                    ulong? compressedSize = null;
                    if ((attributeFlags & 0x8001) != 0)
                    {

                        if (attributeLength < 72 || mappingPairsOffset < 72)
                        {

                            throw new InvalidDataException("Compressed/sparse header is incomplete.");

                        }

                        compressedSize = BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(attributeOffset + 64, 8));

                        output.WriteLine($"Compressed/allocated size: {compressedSize} bytes");

                    }

                    int mappingPairsLength = length - mappingPairsOffset;

                    int mappingPairsStart = attributeOffset + mappingPairsOffset;

                    var runs = ReadDataRuns(record, mappingPairsStart, mappingPairsLength, startingVCN, output);
                    streams.Add(new NtfsDataStream(attributeId, nameLength == 0 ? null : attributeName,
                        attributeFlags, false, startingVCN == 0 ? dataSize : null,
                        startingVCN == 0 ? allocatedSize : null, startingVCN == 0 ? initializedSize : null,
                        compressedSize, startingVCN, endingVCN, compressionUnit, runs));

                    if (output != TextWriter.Null)
                    {
                        output.WriteLine("Raw mapping pairs:");

                        for (int i = 0; i < mappingPairsLength; i++)
                        {

                            byte value = record[attributeOffset + mappingPairsOffset + i];

                            output.WriteLine($"{value:X2}");

                        }

                        output.WriteLine($"Mapping pairs area: {mappingPairsLength} bytes");
                    }

                }

                attributeOffset += length;

            }

            return streams;
        }

        public static void ParseDataRuns(byte[] record, int mappingPairsStart, int mappingPairsLength, ulong startingVCN) =>
            ReadDataRuns(record, mappingPairsStart, mappingPairsLength, startingVCN, Console.Out);

        private static IReadOnlyList<NtfsDataRun> ReadDataRuns(byte[] record, int mappingPairsStart,
            int mappingPairsLength, ulong startingVCN, TextWriter output)
        {

            if (record == null)
            {

                throw new ArgumentNullException(nameof(record));

            }

            if (mappingPairsStart < 0 || mappingPairsLength <= 0 || mappingPairsStart > record.Length || mappingPairsLength > record.Length - mappingPairsStart)
            {

                throw new InvalidDataException("Invalid mapping pairs boundaries.");

            }

            int position = mappingPairsStart;
            
            int end = mappingPairsStart + mappingPairsLength;

            ulong currentVCN = startingVCN;
            
            long currentLCN = 0;

            output.WriteLine();

            output.WriteLine("DECODED DATA RUNS:");

            List<NtfsDataRun> runs = new();

            while (position < end)
            {

                byte header = record[position++];

                if (header == 0)
                {

                    output.WriteLine("End of runlist reached.");
                    return runs;

                }


                int offsetSize = (header >> 4) & 0x0F;

                int lengthSize = header & 0x0F;

                if (lengthSize == 0 || lengthSize > 8 || offsetSize > 8)
                {

                    throw new InvalidDataException("Invalid mapping pairs header.");

                }


                if (lengthSize + offsetSize > end - position)
                {

                    throw new InvalidDataException("Mapping pair extends beyond the runlist.");

                }


                ulong runLength = 0;

                for (int i = 0; i < lengthSize; i++)
                {

                    runLength |= (ulong)record[position++] << (i * 8);

                }


                if (runLength == 0)
                {

                    throw new InvalidDataException("Invalid zero-length data run.");

                }


                bool isSparse = offsetSize == 0;

                if (!isSparse)
                {

                    ulong rawOffset = 0;

                    for (int i = 0; i < offsetSize; i++)
                    {

                        rawOffset |= (ulong)record[position++] << (i * 8);

                    }


                    long lcnOffset;

                    if (offsetSize == 8)
                    {

                        lcnOffset = unchecked((long)rawOffset);

                    }
                    else
                    {

                        int bits = offsetSize * 8;

                        if ((rawOffset & (1UL << (bits - 1))) != 0)
                        {

                            rawOffset |= ulong.MaxValue << bits;

                        }

                        lcnOffset = unchecked((long)rawOffset);
                    }


                    currentLCN = checked(currentLCN + lcnOffset);

                    if (currentLCN < 0)
                    {

                        throw new InvalidDataException("Calculated LCN is negative.");

                    }

                }


                runs.Add(new NtfsDataRun(currentVCN, runLength, isSparse ? null : currentLCN));

                output.WriteLine();

                output.WriteLine($"Run #{runs.Count}");

                output.WriteLine($"Starting VCN: {currentVCN}");

                output.WriteLine($"Cluster count: {runLength}");


                if (isSparse)
                {

                    output.WriteLine("Type: Sparse run");

                    output.WriteLine("No physical LCN assigned.");

                }
                else
                {

                    output.WriteLine("Type: Allocated run");

                    output.WriteLine($"Starting LCN: {currentLCN}");

                }

                currentVCN = checked(currentVCN + runLength);

            }

            throw new InvalidDataException("Runlist terminator was not found.");

        }

    }

}
