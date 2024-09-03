// /Arcs/Repos/Crypto/VinKekFish/src/main/5 main-crypto/exe/auto/disk/disk.cs

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
        public static nint fuse_read(byte*  path, byte*  buffer, nint size, long position, FuseFileInfo * fileInfo)
        {
            var fileName = Utf8StringMarshaller.ConvertToManaged(path);

            if (fileName != vinkekfish_file_path)
            {
                if (fileName == "/")
                    return - (int) PosixResult.EOPNOTSUPP;

                return - (int) PosixResult.ENOENT;
            }

            if (position + size > (long) FileSize)
                size = (nint) ((long) FileSize - position);

            
            // size - общий размер для чтения.
            for (nint i = 0; i < size;)
            {
                var pos = getPosition(i + (nint) position, size - i);

                var fn = GetFileNumberName(pos);
                var cf = GetCatFileNumberName(pos);

                try
                {
                    using (var file = File.Open(fn, FileMode.Open, FileAccess.Read, FileShare.None))
                    {
                        using (var catFile = File.Open(cf, FileMode.Open, FileAccess.Read, FileShare.None))
                        {
                               file.Read(bytesFromFile);
                            catFile.Seek(pos.catPos, SeekOrigin.Begin);
                            catFile.Read(sync1);
                            catFile.Read(sync2);
                        }
                    }
/*
                    // Расшифрование данных
                    DoDecrypt(pos);

                    BytesBuilder.CopyTo(sync2, block);
                    keccakA!.DoXor(block, KeccakPrime.BlockLen);
                    if (!IsNull(block))
                    {
                        Console.WriteLine("Hash is incorrect for block: " + fn);
                        return -(nint)PosixResult.EINTEGRITY;
                    }
*/
                    for (nint j = 0; j < pos.size; j++, i++)
                    {
                        buffer[i] = bytesFromFile[pos.position + j];
                    }
                }
                catch (FileNotFoundException)
                {
                    BytesBuilder.ToNull(pos.size, buffer + i);
                    i += pos.size;
                }
            }

            return size;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
        public static nint fuse_write(byte* path, byte* buffer, nint size, long position, FuseFileInfo * fileInfo)
        {
            var fileName = Utf8StringMarshaller.ConvertToManaged(path);
            if (fileName != vinkekfish_file_path)
            {
                return - (nint) PosixResult.ENOENT;
            }

            if (position + size > (long) FileSize)
                size = (nint) ((long) FileSize - position);

            for (nint i = 0; i < size;)
            {
                var pos = getPosition(i + (nint)position, size - i);

                var fn        = GetFileNumberName(pos);
                var cf        = GetCatFileNumberName(pos);
                var isNull    = false;
                var notExists = false;

                try
                {
                    using (var    file = File.Open(fn, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    using (var catFile = File.Open(cf, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                           file.Read(bytesFromFile);
                        catFile.Seek(pos.catPos, SeekOrigin.Begin);
                        catFile.Read(sync1);
                        catFile.Read(sync2);
                    }
/*
                    // Расшифрование данных
                    DoDecrypt(pos);

                    BytesBuilder.CopyTo(sync2, block);
                    keccakA!.DoXor(block, KeccakPrime.BlockLen);
                    if (!IsNull(block))
                    {
                        Console.WriteLine("Hash is incorrect (in write function) for block: " + fn);
                        return -(nint)PosixResult.EINTEGRITY;
                    }
*/
                    for (nint j = 0; j < pos.size; j++, i++)
                    {
                        bytesFromFile[pos.position + j] = buffer[i];
                    }

                    // Копируем содержимое файла категорий
                    var bcf = SyncBackupName + cf;
                    if (File.Exists(cf))
                        File.Copy(cf, bcf);
                    else
                        File.WriteAllBytes(bcf, nullBlock);

                    using (var    file = File.Open(SyncBackupName + fn, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    using (var catFile = File.Open(                bcf, FileMode.Open,      FileAccess.Write, FileShare.None))
                    {
                        isNull = IsNull(bytesFromFile);
                        if (!isNull)
                        {
                            GenerateNewSync(pos);
                            // DoEncrypt(pos);

                            file.Write(bytesFromFile);

                            catFile.Seek(pos.catPos, SeekOrigin.Begin);
                            catFile.Write(sync3);
                            catFile.Write(sync4);
                        }
                        else
                        {
                            file.Seek(0, SeekOrigin.Begin);
                            file.Write(nullBlock);

                            catFile.Seek (pos.catPos, SeekOrigin.Begin);
                            catFile.Write(nullBlock, 0, FullBlockSyncLen);
                        }
                    }
                }
                catch (FileNotFoundException)
                {
                    notExists = true;
                    for (nint j = 0; j < pos.size; j++, i++)
                    {
                        bytesFromFile[pos.position + j] = buffer[i];
                    }

                    isNull = IsNull(bytesFromFile);

                    if (!isNull)
                    using (var    file = File.Open(fn, FileMode.CreateNew,    FileAccess.ReadWrite, FileShare.None))
                    using (var catFile = File.Open(cf, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                    {
                        if (catFile.Length == 0)
                            catFile.Write(nullBlock);

                        catFile.Seek(pos.catPos, SeekOrigin.Begin);
                        catFile.Read(sync1);
                        catFile.Read(sync2);

                        GenerateNewSync(pos);
                        //DoEncrypt(pos);

                        file.Write(bytesFromFile);
                        catFile.Seek(pos.catPos, SeekOrigin.Begin);
                        catFile.Write(sync3);
                        catFile.Write(sync4);
                    }
                }

#warning Необходимо вставить восстановление после сбоя и проверки на наличие LockFile

                File.WriteAllText(LockFile, "");

                File.Delete(LockFile);
                File.Delete(LockFile);

                File.Delete(LockFile);

#warning Вставить проверку на то, что файл cat также является весь нулевым. И вставить обнуление ячеек файла cat при удалении этого файла.
                if (isNull && !notExists)
                    File.Delete(fn);
            }

            return size;
        }
