using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RecoveryLab;

internal static class Program
{
    // Preserved for debugging. WPF starts through App.xaml.
    internal static void RunConsoleDiagnostics()
    {
        try
        {

            var region = MftScan.ReadRegion((offset, count) => RawDiskReader.ReadBytes(offset, count));
            var volume = region.Volume;
            int bytesPerSector = volume.BytesPerSector;
            long clusterSize = volume.ClusterSize;
            int recordSize = volume.RecordSize;
            long mftOffset = volume.MftOffset;

            // The original one-record diagnostics still use the first raw record.
            byte[] firstMftRecord = region.Bytes.AsSpan(0, recordSize).ToArray();
            byte[] fixedMftRecord = MftRecordParser.ApplyUsaFixups(firstMftRecord, bytesPerSector);
            //---------------------------------------------------------------------------------------------------------------
            Console.WriteLine();
            Console.WriteLine("SEQUENTIAL MFT READING");
            Console.WriteLine("======================");


            Console.WriteLine($"MFT starting offset: {mftOffset}");
            Console.WriteLine($"Bytes read: {region.Bytes.Length}");

            int recordsInRun = region.RecordCount;

            Console.WriteLine($"Records available: {recordsInRun}");

            int validRecords = 0;

            int inUseRecords = 0;

            int invalidRecords = 0;

            int fixupFailures = 0;

            List<(int RecordNumber, byte[] Record)> notInUseRecords = new();

            List<(int RecordNumber, byte[] Record)> inUseFileRecords = new();


            foreach (var slot in MftScan.Inspect(region))
            {
                int i = slot.Number;
                if (slot.FixupError is string fixupError)
                {
                    fixupFailures++;
                    if (fixupFailures <= 3)
                    {
                        Console.WriteLine();
                        Console.WriteLine($"Fixup failure in record #{i}:");
                        Console.WriteLine(fixupError);
                    }

                    continue;
                }

                if (slot.Record is not byte[] correctedRecord || slot.Header is not MftHeader header)
                {
                    invalidRecords++;
                    continue;
                }

                ushort currentSequenceNumber = header.Sequence;
                uint currentUsedSize = header.UsedSize;
                uint currentAllocatedSize = header.AllocatedSize;

                //This small block down there is commented
                /*bool currentInUse = (currentFlags & 0x0001) != 0;

                bool currentIsDirectory = (currentFlags & 0x0002) != 0;

                validRecords++;

                if (currentInUse)
                {

                    inUseRecords++;

                }
                */

                /*bool currentInUse = (currentFlags & 0x0001) != 0;
                bool currentIsDirectory = (currentFlags & 0x0002) != 0;

                validRecords++;

                if (currentInUse)
                {

                    inUseRecords++;

                }
                else
                {

                    notInUseRecords.Add((i, correctedRecord));

                }*/

                bool currentInUse = header.InUse;

                bool currentIsDirectory = header.IsDirectory;

                validRecords++;

                if (currentInUse)

                {
                    inUseRecords++;

                    if (!currentIsDirectory)

                    {

                        inUseFileRecords.Add((i, correctedRecord));

                    }

                }
                else
                {

                    notInUseRecords.Add((i, correctedRecord));

                }


                if (i == 54)
                {

                    Console.WriteLine();
                    Console.WriteLine("TEST FILE DATA INSPECTION");
                    Console.WriteLine("=========================");

                    MftRecordParser.ParseFileName(correctedRecord);
                    MftRecordParser.ParseDataAttribute(correctedRecord);

                }

                if (validRecords <= 12)
                {

                    Console.WriteLine();

                    Console.WriteLine($"MFT RECORD #{i}");

                    Console.WriteLine($"Sequence Number: {currentSequenceNumber}");

                    Console.WriteLine($"In Use: {currentInUse}");

                    Console.WriteLine($"Directory: {currentIsDirectory}");

                    Console.WriteLine($"Used Size: {currentUsedSize}");

                    Console.WriteLine($"Allocated Size: {currentAllocatedSize}");

                }
            }


            Console.WriteLine();
            Console.WriteLine("MFT READING SUMMARY");
            Console.WriteLine("===================");
            Console.WriteLine($"Records examined: {recordsInRun}");
            Console.WriteLine($"Valid FILE records: {validRecords}");
            Console.WriteLine($"In-use records: {inUseRecords}");
            Console.WriteLine($"Unused or invalid slots: {invalidRecords}");
            Console.WriteLine($"USA fixup failures: {fixupFailures}");

            Console.WriteLine();
            Console.WriteLine("IN-USE FILE RECORDS");
            Console.WriteLine("===================");

            foreach (var file in inUseFileRecords)
            {

                Console.WriteLine();

                Console.WriteLine($"MFT Record Number: {file.RecordNumber}");

                try
                {

                    MftRecordParser.ParseFileName(file.Record);

                }
                catch (InvalidDataException ex)
                {

                    Console.WriteLine($"Could not parse filename: {ex.Message}");

                }

            }

            Console.WriteLine();
            Console.WriteLine("NOT-IN-USE RECORDS");
            Console.WriteLine("==================");
            //---------------------------------------------------------------------------------------------------
            if (notInUseRecords.Count == 0)
            {

                Console.WriteLine("No candidates found.");

            }
            else
            {

                foreach (var candidate in notInUseRecords)
                {

                    Console.WriteLine();
                    Console.WriteLine($"MFT Record Number: {candidate.RecordNumber}");

                    ushort candidateSequenceNumber = BinaryPrimitives.ReadUInt16LittleEndian(candidate.Record.AsSpan(16, 2));

                    ushort candidateFlags = BinaryPrimitives.ReadUInt16LittleEndian(candidate.Record.AsSpan(22, 2));

                    Console.WriteLine($"Sequence Number: {candidateSequenceNumber}");

                    Console.WriteLine($"Flags: 0x{candidateFlags:X4}");

                    Console.WriteLine();

                    Console.WriteLine("CANDIDATE FILE NAME:");

                    try
                    {

                        MftRecordParser.ParseFileName(candidate.Record);

                        Console.WriteLine();

                        Console.WriteLine("CANDIDATE DATA INFORMATION:");


                        // This try there is commented
                        /*try
                        {

                            MftRecordParser.ParseDataAttribute(candidate.Record);
        //-----------------------------------------------------------------------------------------------------------------------
                            Console.WriteLine();

                            Console.WriteLine("RAW DATA RUN INSPECTION");

                            ulong startingLcn = 2680;

                            int bytesToRead = 64;

                            long dataOffset = checked((long)(startingLcn * (ulong)clusterSize));

                            Console.WriteLine($"Starting LCN: {startingLcn}");

                            Console.WriteLine($"Cluster size: {clusterSize} bytes");

                            Console.WriteLine($"Byte offset: {dataOffset}");

                            Console.WriteLine($"Bytes requested: {bytesToRead}");

                            byte[] rawData = RawDiskReader.ReadBytes(dataOffset,bytesToRead);

                            Console.WriteLine();

                            Console.WriteLine("READ DATA (HEX):");

                            for (int i = 0; i < rawData.Length; i++)
                            {

                                Console.Write($"{rawData[i]:X2} ");

                                if ((i + 1) % 16 == 0)
                                {

                                    Console.WriteLine();

                                }

                            }

                            Console.WriteLine();

                        }
                        catch (InvalidDataException ex)
                        {

                            Console.WriteLine($"Could not parse DATA attribute: {ex.Message}");

                        }*/

                    }
                    catch (InvalidDataException ex)
                    {

                        Console.WriteLine($"Could not parse filename: {ex.Message}");

                    }

                }

            }
            //---------------------------------------------------------------------------------------------------------------------------
            MftRecordParser.ParseDataAttribute(fixedMftRecord);
            //--------------------------------------------------------------------------------------------------------------------------
            MftRecordParser.ParseFileName(fixedMftRecord);
            //-------------------------------------------------------------------------------------------------------------------------
            MftRecordParser.ParseStandardInformation(fixedMftRecord);
            //----------------------------------------------------------------------------------------------------------------------------
            MftRecordParser.PrintAttributes(fixedMftRecord);
        //----------------------------------------------------------------------------------------------------------------------------
            Console.WriteLine();

            Console.WriteLine("USA FIXUP TEST: ");

            Console.WriteLine("Original sector endings:");

            Console.WriteLine($"Sector 1: {Convert.ToHexString(firstMftRecord.AsSpan(510, 2))}");

            Console.WriteLine($"Sector 2: {Convert.ToHexString(firstMftRecord.AsSpan(1022, 2))}");

            Console.WriteLine("Corrected sector endings:");

            Console.WriteLine($"Sector 1: {Convert.ToHexString(fixedMftRecord.AsSpan(510, 2))}");

            Console.WriteLine($"Sector 2: {Convert.ToHexString(fixedMftRecord.AsSpan(1022, 2))}");
            //---------------------------------------------------------------------------------------------------------------------------------
            Console.WriteLine();

            Console.WriteLine("First 64 bytes of the MFT record :");

            for (int i = 0; i  < 64; i++)
            {

                Console.WriteLine($"{firstMftRecord[i]:x2}");

                if ((i + 1) % 16 == 0)
                {

                    Console.WriteLine();

                }

            }
        //----------------------------------------------------------------------------------------------------------------------------------
            ushort usaOffset = BinaryPrimitives.ReadUInt16LittleEndian(firstMftRecord.AsSpan(4,2));

            ushort usaCount = BinaryPrimitives.ReadUInt16LittleEndian(firstMftRecord.AsSpan(6,2));

            ulong logSequenceNumber = BinaryPrimitives.ReadUInt64LittleEndian(firstMftRecord.AsSpan(8,8));

            ushort sequenceNumber = BinaryPrimitives.ReadUInt16LittleEndian(firstMftRecord.AsSpan(16,2));

            ushort hardLinkCount = BinaryPrimitives.ReadUInt16LittleEndian(firstMftRecord.AsSpan(18,2));

            ushort firstAttributeOffset = BinaryPrimitives.ReadUInt16LittleEndian(firstMftRecord.AsSpan(20,2));

            ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(firstMftRecord.AsSpan(22,2));

            uint usedSize = BinaryPrimitives.ReadUInt32LittleEndian(firstMftRecord.AsSpan(24,4));

            uint allocatedSize = BinaryPrimitives.ReadUInt32LittleEndian(firstMftRecord.AsSpan(28,4));

            Console.WriteLine();

            Console.WriteLine("MFT RECORD HEADER: ");

            Console.WriteLine($"USA Offset: {usaOffset}");

            Console.WriteLine($"USA Count: {usaCount}");

            Console.WriteLine($"Log Sequence Number: {logSequenceNumber}");

            Console.WriteLine($"Sequence Number: {sequenceNumber}");

            Console.WriteLine($"Hard Link Count: {hardLinkCount}");

            Console.WriteLine($"First Attribute Offset: {firstAttributeOffset}");

            Console.WriteLine($"Flags: {flags}");

            Console.WriteLine($"Used Size: {usedSize}");

            Console.WriteLine($"Allocated Size: {allocatedSize}");
            //-----------------------------------------------------------------------------------------------------------------------------------
            string recordSignature = Encoding.ASCII.GetString(firstMftRecord, 0, 4);

            Console.WriteLine();

            Console.WriteLine("MFT Information: ");

            Console.WriteLine($"Total Sectors: {volume.TotalSectors}");

            Console.WriteLine($"MFT Starting Cluster: {volume.MftStartCluster}");

            Console.WriteLine($"MT Mirror Cluster: {volume.MftMirrorCluster}");

            Console.WriteLine($"Cluster Size: {clusterSize} bytes");

            Console.WriteLine($"MFT Record Size: {recordSize} bytes");

            Console.WriteLine($"MFT byte offset: {mftOffset}");

            Console.WriteLine($"First MFT record signature: {recordSignature}");


            //----------------------------------------------------------------------------------------------------------------------------

            Console.WriteLine("Leitura de Boot Sector efetuada com sucesso");

            string oemId = volume.OemId;

            //ushort bytesPerSector = BitConverter.ToUInt16(bootSector, 11);

            //byte sectorsPerCluster = bootSector[13];

            ushort signature = volume.BootSignature;

            Console.WriteLine($"OEM ID: {oemId}");

            Console.WriteLine($"Bytes per sector: {bytesPerSector}");

            Console.WriteLine($"Sectors per cluster: {volume.SectorsPerCluster}");

            Console.WriteLine($"Boot sector signature: 0x{signature:x4}");

        }
        catch (Exception ex)
        {

            Console.WriteLine("An error occured");
            Console.WriteLine(ex.Message);

        }
    }
}
