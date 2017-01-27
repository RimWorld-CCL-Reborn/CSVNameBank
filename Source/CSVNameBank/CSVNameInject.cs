using Verse;
using RimWorld;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Collections.Generic;
using System;
using Verse.Grammar;

namespace CSVNameBank
{
    [StaticConstructorOnStartup]
    static class CSVNameInject
    {
        static Dictionary<Gender, List<NameTriple>> solidNames;
        static Dictionary<Gender, Queue<NameTriple>> usedNames;

        static CSVNameInject()
        {

            SolidBioDatabase.allBios.Clear();


            // FieldInfo fieldinfo = typeof(NameBank).GetField("names", BindingFlags.Instance | BindingFlags.NonPublic);
            // fieldinfo.SetValue(PawnNameDatabaseShuffled.BankOf(PawnNameCategory.HumanStandard), null);

            Detours.TryDetourFromTo(typeof(PawnBioAndNameGenerator).GetMethod("GeneratePawnName"), typeof(CSVNameInject).GetMethod("_GeneratePawnName"));

            solidNames = new Dictionary<Gender, List<NameTriple>>();
            usedNames = new Dictionary<Gender, Queue<NameTriple>>();

            foreach (var g in Enum.GetValues(typeof(Gender)))
            {
                solidNames.Add((Gender)g, new List<NameTriple>());
                usedNames.Add((Gender)g, new Queue<NameTriple>());
            }

            String path = Path.Combine(Path.Combine(GenFilePaths.CoreModsFolderPath, "CSVNameBank"), "NameDatabase.csv");
            //Log.Message(path);

            string[] allLines = ReadAllLinesSavely(path);
            //File.ReadAllLines(path);
            Log.Message("Logs found: " + allLines.Count());
            //Log.Message("Line for testing purposes: " + allLines[0]);
            //Log.Message(allLines[0].Split(';').Count().ToString());
            var query = (from line in allLines
                         let data = line.Split(',')
                         select new
                         {
                             FirstName = data[0],
                             NickName = data[1],
                             LastName = data[2],
                             Gender = data[3].NullOrEmpty() ? Gender.Male : Enum.Parse(typeof(Gender), data[3])
                         }).ToList();

            query.ForEach(g =>
                {
                    if (!g.FirstName.NullOrEmpty() && !g.LastName.NullOrEmpty())
                        solidNames[(Gender)g.Gender].Add(new NameTriple(g.FirstName, g.NickName.NullOrEmpty() ? g.FirstName : g.NickName, g.LastName));
                });

            solidNames.Keys.ToList().ForEach(g =>
            {
                Log.Message(g.ToString()+ "s added: " + solidNames[g].Count);
            });
        }

        public static string[] ReadAllLinesSavely(string path)
        {
            using (var csv = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sr = new StreamReader(csv))
            {
                sr.ReadLine();
                List<string> file = new List<string>();
                while (!sr.EndOfStream)
                {
                    file.Add(sr.ReadLine());
                }

                return file.ToArray();
            }
        }

        public static Name _GeneratePawnName(Pawn pawn, NameStyle style = NameStyle.Full, string forcedLastName = null)
        {
            if (style == NameStyle.Full)
            {
                NameTriple name;

                if (solidNames[pawn.gender].Count <= 0)
                {
                   
                    Log.Message(pawn.gender.ToString() + " Name database reset");
                    Messages.Message(pawn.gender.ToString() + " Name database reset", MessageSound.Silent);

                    while (usedNames[pawn.gender].Count > 0)
                    {
                        solidNames[pawn.gender].Add(usedNames[pawn.gender].Dequeue());
                    }
                }
                
                solidNames[pawn.gender].TryRandomElement(out name);

                solidNames[pawn.gender].Remove(name);
                usedNames[pawn.gender].Enqueue(name);
                return name;
            }
            else
            {
                if (style == NameStyle.Numeric)
                {
                    int num = 1;
                    string text;
                    while (true)
                    {
                        text = pawn.KindLabel + " " + num.ToString();
                        if (!NameUseChecker.NameSingleIsUsedOnAnyMap(text))
                        {
                            break;
                        }
                        num++;
                    }
                    return new NameSingle(text, true);
                }
                throw new InvalidOperationException();
            }
        }
    }

    public static class Detours
    {

        private static List<string> detoured = new List<string>();
        private static List<string> destinations = new List<string>();

        /**
            _this is a basic first implementation of the IL method 'hooks' (detours) made possible by RawCode's work;
            https://ludeon.com/forums/index.php?topic=17143.0

            Performs detours, spits out basic logs and warns if a method is detoured multiple times.
        **/
        public static unsafe bool TryDetourFromTo(MethodInfo source, MethodInfo destination)
        {
            // error out on null arguments
            if (source == null)
            {
                Log.Message("Source MethodInfo is null");
                return false;
            }

            if (destination == null)
            {
                Log.Message("Destination MethodInfo is null");
                return false;
            }

            // keep track of detours and spit out some messaging
            string sourceString = source.DeclaringType.FullName + "." + source.Name + " @ 0x" + source.MethodHandle.GetFunctionPointer().ToString("X" + (IntPtr.Size * 2).ToString());
            string destinationString = destination.DeclaringType.FullName + "." + destination.Name + " @ 0x" + destination.MethodHandle.GetFunctionPointer().ToString("X" + (IntPtr.Size * 2).ToString());


            if (detoured.Contains(sourceString))
            {
                Log.Warning("Source method ('" + sourceString + "') is previously detoured to '" + destinations[detoured.IndexOf(sourceString)] + "'");
            }

            //Log.Message("Detouring '" + sourceString + "' to '" + destinationString + "'");


            detoured.Add(sourceString);
            destinations.Add(destinationString);

            if (IntPtr.Size == sizeof(Int64))
            {
                // 64-bit systems use 64-bit absolute address and jumps
                // 12 byte destructive

                // Get function pointers
                long Source_Base = source.MethodHandle.GetFunctionPointer().ToInt64();
                long Destination_Base = destination.MethodHandle.GetFunctionPointer().ToInt64();

                // Native source address
                byte* Pointer_Raw_Source = (byte*)Source_Base;

                // Pointer to insert jump address into native code
                long* Pointer_Raw_Address = (long*)(Pointer_Raw_Source + 0x02);

                // Insert 64-bit absolute jump into native code (address in rax)
                // mov rax, immediate64
                // jmp [rax]
                *(Pointer_Raw_Source + 0x00) = 0x48;
                *(Pointer_Raw_Source + 0x01) = 0xB8;
                *Pointer_Raw_Address = Destination_Base; // ( Pointer_Raw_Source + 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09 )
                *(Pointer_Raw_Source + 0x0A) = 0xFF;
                *(Pointer_Raw_Source + 0x0B) = 0xE0;

            }
            else
            {
                // 32-bit systems use 32-bit relative offset and jump
                // 5 byte destructive

                // Get function pointers
                int Source_Base = source.MethodHandle.GetFunctionPointer().ToInt32();
                int Destination_Base = destination.MethodHandle.GetFunctionPointer().ToInt32();

                // Native source address
                byte* Pointer_Raw_Source = (byte*)Source_Base;

                // Pointer to insert jump address into native code
                int* Pointer_Raw_Address = (int*)(Pointer_Raw_Source + 1);

                // Jump offset (less instruction size)
                int offset = (Destination_Base - Source_Base) - 5;

                // Insert 32-bit relative jump into native code
                *Pointer_Raw_Source = 0xE9;
                *Pointer_Raw_Address = offset;
            }

            // done!
            return true;
        }

    }
}