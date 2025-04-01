// Copyright (C) 2025, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.

using System;
using System.IO;
using System.Security.Cryptography;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    public class Issue4693 : BasicSetupHelper
    {
        [Test]
        [Category("Targeted")]
        public void RunCommandsOriginal()
        {
            var testopts = TestOptions.Expand(new { no_encryption = true, keep_versions = 1, dblock_size = "5mb" });

            File.WriteAllText(Path.Combine(DATAFOLDER, "A.txt"), "A");
            File.WriteAllText(Path.Combine(DATAFOLDER, "B.txt"), "B");
            var buffer = new byte[1024 * 1024 * 12];
            Random.Shared.NextBytes(buffer);
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "b1.bin"), buffer);

            var a_hash = Convert.ToBase64String(SHA256.HashData(File.ReadAllBytes(Path.Combine(DATAFOLDER, "A.txt"))));

            // Backup 1
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Backup([DATAFOLDER]));

            File.Delete(Path.Combine(DATAFOLDER, "A.txt"));
            File.WriteAllText(Path.Combine(DATAFOLDER, "C.txt"), "C");

            Random.Shared.NextBytes(buffer);
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "b2.bin"), buffer);

            // Backup 2
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Backup([DATAFOLDER]));

            File.WriteAllText(Path.Combine(DATAFOLDER, "A.txt"), "A");
            Random.Shared.NextBytes(buffer);
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "b3.bin"), buffer);

            if (a_hash != Convert.ToBase64String(SHA256.HashData(File.ReadAllBytes(Path.Combine(DATAFOLDER, "A.txt")))))
                throw new Exception("Hash of A.txt changed");

            // Backup 3
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Backup([DATAFOLDER]));

            File.Delete(Path.Combine(DATAFOLDER, "b1.bin"));
            File.Delete(Path.Combine(DATAFOLDER, "b2.bin"));

            // Backup 4
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var res = c.Backup([DATAFOLDER]);
                TestUtils.AssertResults(res);
                Assert.That(res.CompactResults.DeletedFileCount, Is.GreaterThan(0), "Compact was not run");
            }

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Test(long.MaxValue));

            // Test we can recrate without errors
            File.Delete(DBFILE);
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Repair());

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Test(long.MaxValue));
        }

        [Test]
        [Category("Targeted")]
        public void RunCommandsSimplified()
        {
            var testopts = TestOptions.Expand(new { no_encryption = true, keep_versions = 1, dblock_size = "5mb" });

            File.WriteAllText(Path.Combine(DATAFOLDER, "A.txt"), "A");
            File.WriteAllText(Path.Combine(DATAFOLDER, "B.txt"), "B");

            // Backup 1
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Backup([DATAFOLDER]));

            File.Delete(Path.Combine(DATAFOLDER, "A.txt"));
            File.WriteAllText(Path.Combine(DATAFOLDER, "C.txt"), "C");

            // Backup 2
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Backup([DATAFOLDER]));

            File.WriteAllText(Path.Combine(DATAFOLDER, "A.txt"), "A");

            // Backup 3
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Backup([DATAFOLDER]));

            // using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            //     TestUtils.AssertResults(c.Test(long.MaxValue));

            // Test we can recrate without errors
            File.Delete(DBFILE);
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Repair());

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Test(long.MaxValue));
        }
    }
}

