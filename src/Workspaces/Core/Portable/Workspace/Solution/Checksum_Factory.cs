﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.CodeAnalysis.PooledObjects;
using Microsoft.CodeAnalysis.Serialization;
using Roslyn.Utilities;
using System.Diagnostics;

namespace Microsoft.CodeAnalysis
{
    // various factory methods. all these are just helper methods
    internal partial class Checksum
    {
        private static readonly ObjectPool<IncrementalHash> s_incrementalHashPool =
            new(() => IncrementalHash.CreateHash(HashAlgorithmName.SHA256), size: 20);

        private static readonly ObjectPool<byte[]> s_twoChecksumByteArrayPool =
            new(() => new byte[HashSize * 2]);
        private static readonly ObjectPool<byte[]> s_threeChecksumByteArrayPool =
            new(() => new byte[HashSize * 3]);

        public static Checksum Create(IEnumerable<string> values)
        {
            using var pooledHash = s_incrementalHashPool.GetPooledObject();
            using var pooledBuffer = SharedPools.ByteArray.GetPooledObject();
            var hash = pooledHash.Object;

            foreach (var value in values)
            {
                AppendData(hash, pooledBuffer.Object, value);
                AppendData(hash, pooledBuffer.Object, "\0");
            }

            return From(hash.GetHashAndReset());
        }

        public static Checksum Create(string value)
        {
            using var pooledHash = s_incrementalHashPool.GetPooledObject();
            using var pooledBuffer = SharedPools.ByteArray.GetPooledObject();
            var hash = pooledHash.Object;

            AppendData(hash, pooledBuffer.Object, value);

            return From(hash.GetHashAndReset());
        }

        public static Checksum Create(Stream stream)
        {
            using var pooledHash = s_incrementalHashPool.GetPooledObject();
            using var pooledBuffer = SharedPools.ByteArray.GetPooledObject();

            var hash = pooledHash.Object;

            var buffer = pooledBuffer.Object;
            var bufferLength = buffer.Length;
            int bytesRead;
            do
            {
                bytesRead = stream.Read(buffer, 0, bufferLength);
                if (bytesRead > 0)
                {
                    hash.AppendData(buffer, 0, bytesRead);
                }
            }
            while (bytesRead > 0);

            var bytes = hash.GetHashAndReset();

            // if bytes array is bigger than certain size, checksum
            // will truncate it to predetermined size. for more detail,
            // see the Checksum type
            //
            // the truncation can happen since different hash algorithm or 
            // same algorithm on different platform can have different hash size
            // which might be bigger than the Checksum HashSize.
            //
            // hash algorithm used here should remain functionally correct even
            // after the truncation
            return From(bytes);
        }

        public static Checksum Create(IObjectWritable @object)
        {
            using var stream = SerializableBytes.CreateWritableStream();

            using (var objectWriter = new ObjectWriter(stream, leaveOpen: true))
            {
                @object.WriteTo(objectWriter);
            }

            stream.Position = 0;
            return Create(stream);
        }

        public static Checksum Create(Checksum checksum1, Checksum checksum2)
        {
#if NET
            return CreateUsingSpans(checksum1, checksum2);

#else
            return CrateUsingByteArrays(checksum1, checksum2);
#endif
        }

        private static Checksum CrateUsingByteArrays(Checksum checksum1, Checksum checksum2)
        {
            var hash = s_incrementalHashPool.Allocate();

            var bytes = s_twoChecksumByteArrayPool.Allocate();
            var bytesSpan = bytes.AsSpan();
            checksum1.WriteTo(bytesSpan);
            checksum2.WriteTo(bytesSpan.Slice(HashSize));

            hash.AppendData(bytes);
            var hashResult = hash.GetHashAndReset();

            s_incrementalHashPool.Free(hash);
            s_twoChecksumByteArrayPool.Free(bytes);

            return From(hashResult);
        }

        private static Checksum CrateUsingByteArrays(Checksum checksum1, Checksum checksum2, Checksum checksum3)
        {
            var hash = s_incrementalHashPool.Allocate();

            var bytes = s_threeChecksumByteArrayPool.Allocate();
            var bytesSpan = bytes.AsSpan();
            checksum1.WriteTo(bytesSpan);
            checksum2.WriteTo(bytesSpan.Slice(HashSize));
            checksum3.WriteTo(bytesSpan.Slice(2 * HashSize));

            hash.AppendData(bytes);
            var hashResult = hash.GetHashAndReset();

            s_incrementalHashPool.Free(hash);
            s_threeChecksumByteArrayPool.Free(bytes);

            return From(hashResult);
        }

#if NET

        private static Checksum CreateUsingSpans(Checksum checksum1, Checksum checksum2)
        {
            var hash = s_incrementalHashPool.Allocate();
            Span<byte> bytesSpan = stackalloc byte[2 * HashSize];
            Span<byte> hashResultSpan = stackalloc byte[hash.HashLengthInBytes];

            checksum1.WriteTo(bytesSpan);
            checksum2.WriteTo(bytesSpan.Slice(HashSize));

            hash.AppendData(bytesSpan);
            hash.GetHashAndReset(hashResultSpan);

            s_incrementalHashPool.Free(hash);

            return From(hashResultSpan);
        }

        private static Checksum CreateUsingSpans(Checksum checksum1, Checksum checksum2, Checksum checksum3)
        {
            var hash = s_incrementalHashPool.Allocate();
            Span<byte> bytesSpan = stackalloc byte[3 * HashSize];
            Span<byte> hashResultSpan = stackalloc byte[hash.HashLengthInBytes];

            checksum1.WriteTo(bytesSpan);
            checksum2.WriteTo(bytesSpan.Slice(HashSize));
            checksum3.WriteTo(bytesSpan.Slice(2 * HashSize));

            hash.AppendData(bytesSpan);
            hash.GetHashAndReset(hashResultSpan);

            s_incrementalHashPool.Free(hash);

            return From(hashResultSpan);
        }

#endif

        public static Checksum Create(Checksum checksum1, Checksum checksum2, Checksum checksum3)
        {
            using var stream = SerializableBytes.CreateWritableStream();

            using (var writer = new ObjectWriter(stream, leaveOpen: true))
            {
                checksum1.WriteTo(writer);
                checksum2.WriteTo(writer);
                checksum3.WriteTo(writer);
            }

            stream.Position = 0;
            return Create(stream);
        }

        public static Checksum Create(IEnumerable<Checksum> checksums)
        {
            using var stream = SerializableBytes.CreateWritableStream();

            using (var writer = new ObjectWriter(stream, leaveOpen: true))
            {
                foreach (var checksum in checksums)
                    checksum.WriteTo(writer);
            }

            stream.Position = 0;
            return Create(stream);
        }

        public static Checksum Create(ImmutableArray<byte> bytes)
        {
            using var stream = SerializableBytes.CreateWritableStream();

            using (var writer = new ObjectWriter(stream, leaveOpen: true))
            {
                for (var i = 0; i < bytes.Length; i++)
                    writer.WriteByte(bytes[i]);
            }

            stream.Position = 0;
            return Create(stream);
        }

        public static Checksum Create<T>(T value, ISerializerService serializer)
        {
            using var stream = SerializableBytes.CreateWritableStream();
            using var context = new SolutionReplicationContext();

            using (var objectWriter = new ObjectWriter(stream, leaveOpen: true))
            {
                serializer.Serialize(value!, objectWriter, context, CancellationToken.None);
            }

            stream.Position = 0;
            return Create(stream);
        }

        public static Checksum Create(ParseOptions value, ISerializerService serializer)
        {
            using var stream = SerializableBytes.CreateWritableStream();

            using (var objectWriter = new ObjectWriter(stream, leaveOpen: true))
            {
                serializer.SerializeParseOptions(value, objectWriter);
            }

            stream.Position = 0;
            return Create(stream);
        }

        private static void AppendData(IncrementalHash hash, byte[] buffer, string value)
        {
            var stringBytes = MemoryMarshal.AsBytes(value.AsSpan());
            Debug.Assert(stringBytes.Length == value.Length * 2);

            var index = 0;
            while (index < stringBytes.Length)
            {
                var remaining = stringBytes.Length - index;
                var toCopy = Math.Min(remaining, buffer.Length);

                stringBytes.Slice(index, toCopy).CopyTo(buffer);
                hash.AppendData(buffer, 0, toCopy);

                index += toCopy;
            }
        }

        public static class TestAccessor
        {
            public static Checksum CrateUsingByteArrays(Checksum checksum1, Checksum checksum2)
                => Checksum.CrateUsingByteArrays(checksum1, checksum2);

            public static Checksum CrateUsingByteArrays(Checksum checksum1, Checksum checksum2, Checksum checksum3)
                => Checksum.CrateUsingByteArrays(checksum1, checksum2, checksum3);

#if NET

            public static Checksum CreateUsingSpans(Checksum checksum1, Checksum checksum2)
                => Checksum.CreateUsingSpans(checksum1, checksum2);

            public static Checksum CreateUsingSpans(Checksum checksum1, Checksum checksum2, Checksum checksum3)
                => Checksum.CreateUsingSpans(checksum1, checksum2, checksum3);

#endif
        }
    }
}
