﻿#region BSD Licence
/* Copyright (c) 2013, Doxense SARL
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:
	* Redistributions of source code must retain the above copyright
	  notice, this list of conditions and the following disclaimer.
	* Redistributions in binary form must reproduce the above copyright
	  notice, this list of conditions and the following disclaimer in the
	  documentation and/or other materials provided with the distribution.
	* Neither the name of the <organization> nor the
	  names of its contributors may be used to endorse or promote products
	  derived from this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL <COPYRIGHT HOLDER> BE LIABLE FOR ANY
DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */
#endregion

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace FoundationDb.Client.Native
{
	internal static unsafe class FdbNative
	{
		public const int FDB_API_VERSION = 21;

		private const string DLL_X86 = "fdb_c.dll";
		private const string DLL_X64 = "fdb_c.dll";

		/// <summary>Handle on the native FDB C API library</summary>
		private static readonly UnmanagedLibrary FdbCLib;

		/// <summary>Exception that was thrown when we last tried to load the native FDB C library (or null if nothing wrong happened)</summary>
		private static readonly Exception LibraryLoadError;

		/// <summary>Holds all the delegate types for the binding with the native C API</summary>
		public static class Delegates
		{

			public delegate byte* FdbGetError(FdbError code);

			public delegate FdbError FdbSelectApiVersionImpl(int runtimeVersion, int headerVersion);

			public delegate int FdbGetMaxApiVersion();

			public delegate FdbError FdbNetworkSetOption(FdbNetworkOption option, byte* value, int value_length);
			public delegate FdbError FdbSetupNetwork();
			public delegate FdbError FdbRunNetwork();
			public delegate FdbError FdbStopNetwork();

			public delegate void FdbFutureDestroy(/*FDBFuture*/IntPtr futureHandle);
			public delegate bool FdbFutureIsReady(FutureHandle futureHandle);
			public delegate bool FdbFutureIsError(FutureHandle futureHandle);
			public delegate FdbError FdbFutureGetError(FutureHandle future, byte** description);
			public delegate FdbError FdbFutureBlockUntilReady(FutureHandle futureHandle);
			public delegate FdbError FdbFutureSetCallback(FutureHandle future, /*FDBCallback*/ IntPtr callback, IntPtr callbackParameter);

			public delegate /*Future*/IntPtr FdbCreateCluster(byte* clusterFilePath);
			public delegate void FdbClusterDestroy(/*FDBCluster*/IntPtr cluster);
			public delegate FdbError FdbClusterSetOption(ClusterHandle cluster, FdbClusterOption option, byte* value, int valueLength);
			public delegate FdbError FdbFutureGetCluster(FutureHandle future, out /*FDBCluster*/IntPtr cluster);
			public delegate void FdbDatabaseDestroy(/*FDBDatabase*/IntPtr database);
			public delegate /*Future*/IntPtr FdbClusterCreateDatabase(ClusterHandle cluster, byte* dbName, int dbNameLength);
			public delegate FdbError FdbFutureGetDatabase(/*Future*/IntPtr future, out /*FDBDatabase*/IntPtr database);
			public delegate FdbError FdbDatabaseCreateTransaction(DatabaseHandle database, out IntPtr transaction);
			public delegate void FdbTransactionSet(TransactionHandle transaction, byte* keyName, int keyNameLength, byte* value, int valueLength);
			public delegate /*Future*/IntPtr FdbTransactionCommit(TransactionHandle transaction);
			public delegate FdbError FdbTransactionGetCommmittedVersion(TransactionHandle transaction, out long version);
			public delegate /*Future*/IntPtr FdbTransactionGetReadVersion(TransactionHandle transaction);
			public delegate FdbError FdbFutureGetVersion(FutureHandle future, out long version);
			public delegate /*Future*/IntPtr FdbTransactionGet(TransactionHandle transaction, byte* keyName, int keyNameLength, bool snapshot);
			public delegate FdbError FdbFutureGetValue(FutureHandle future, out bool present, out byte* value, out int valueLength);
			public delegate /*Future*/IntPtr FdbTransactionGetKey(TransactionHandle transaction, byte* keyName, int keyNameLength, bool orEqual, int offset, bool snapshot);
			public delegate FdbError FdbFutureGetKey(FutureHandle future, out byte* key, out int keyLength);
			public delegate void FdbTransactionClear(TransactionHandle transaction, byte* keyName, int keyNameLength);
			public delegate FdbError FdbFutureGetKeyValue(FutureHandle future, out FdbKeyValue* kv, out int count, out bool more);

			public delegate FdbError FdbTransactionGetRange(
				TransactionHandle transaction,
				byte* beginKeyName, int beginKeyNameLength, bool beginOrEqual, int beginOffset,
				byte* endKeyName, int endKeyNameLength, bool endOrEqual, int endOffset,
				int limit, int targetBytes, FDBStreamingMode mode, int iteration, bool snapshot, bool reverse
			);

		}

		/// <summary>Contain all the stubs to the methods exposed by the C API library</summary>
		public static class Stubs
		{

			// Core

			public static Delegates.FdbSelectApiVersionImpl fdb_select_api_version_impl;
			public static Delegates.FdbGetMaxApiVersion fdb_get_max_api_version;
			public static Delegates.FdbGetError fdb_get_error;

			// Network

			public static Delegates.FdbNetworkSetOption fdb_network_set_option;
			public static Delegates.FdbSetupNetwork fdb_setup_network;
			public static Delegates.FdbRunNetwork fdb_run_network;
			public static Delegates.FdbStopNetwork fdb_stop_network;

			// Cluster

			public static Delegates.FdbCreateCluster fdb_create_cluster;
			public static Delegates.FdbClusterDestroy fdb_cluster_destroy;
			public static Delegates.FdbClusterSetOption fdb_cluster_set_option;
			public static Delegates.FdbClusterCreateDatabase fdb_cluster_create_database;

			// Database

			public static Delegates.FdbDatabaseDestroy fdb_database_destroy;
			public static Delegates.FdbDatabaseCreateTransaction fdb_database_create_transaction;

			// Transaction
			public static Delegates.FdbTransactionGetReadVersion fdb_transaction_get_read_version;
			public static Delegates.FdbTransactionGet fdb_transaction_get;
			public static Delegates.FdbTransactionGetKey fdb_transaction_get_key;
			public static Delegates.FdbTransactionGetRange fdb_transaction_get_range;
			public static Delegates.FdbTransactionSet fdb_transaction_set;
			public static Delegates.FdbTransactionClear fdb_transaction_clear;
			public static Delegates.FdbTransactionCommit fdb_transaction_commit;
			public static Delegates.FdbTransactionGetCommmittedVersion fdb_transaction_get_committed_version;

			// Future

			public static Delegates.FdbFutureDestroy fdb_future_destroy;
			public static Delegates.FdbFutureBlockUntilReady fdb_future_block_until_ready;
			public static Delegates.FdbFutureIsReady fdb_future_is_ready;
			public static Delegates.FdbFutureIsError fdb_future_is_error;
			public static Delegates.FdbFutureSetCallback fdb_future_set_callback;
			public static Delegates.FdbFutureGetError fdb_future_get_error;
			public static Delegates.FdbFutureGetVersion fdb_future_get_version;
			public static Delegates.FdbFutureGetKey fdb_future_get_key;
			public static Delegates.FdbFutureGetCluster fdb_future_get_cluster;
			public static Delegates.FdbFutureGetDatabase fdb_future_get_database;
			public static Delegates.FdbFutureGetValue fdb_future_get_value;

			public static void LoadBindings(UnmanagedLibrary lib)
			{
				lib.Bind(ref Stubs.fdb_get_error, "fdb_get_error");
				lib.Bind(ref Stubs.fdb_select_api_version_impl, "fdb_select_api_version_impl");
				lib.Bind(ref Stubs.fdb_get_max_api_version, "fdb_get_max_api_version");

				lib.Bind(ref Stubs.fdb_network_set_option, "fdb_network_set_option");
				lib.Bind(ref Stubs.fdb_setup_network, "fdb_setup_network");
				lib.Bind(ref Stubs.fdb_run_network, "fdb_run_network");
				lib.Bind(ref Stubs.fdb_stop_network, "fdb_stop_network");

				lib.Bind(ref Stubs.fdb_future_destroy, "fdb_future_destroy");
				lib.Bind(ref Stubs.fdb_future_is_error, "fdb_future_is_error");
				lib.Bind(ref Stubs.fdb_future_is_ready, "fdb_future_is_ready");
				lib.Bind(ref Stubs.fdb_future_block_until_ready, "fdb_future_block_until_ready");
				lib.Bind(ref Stubs.fdb_future_get_error, "fdb_future_get_error");
				lib.Bind(ref Stubs.fdb_future_set_callback, "fdb_future_set_callback");

				lib.Bind(ref Stubs.fdb_create_cluster, "fdb_create_cluster");
				lib.Bind(ref Stubs.fdb_cluster_destroy, "fdb_cluster_destroy");
				lib.Bind(ref Stubs.fdb_cluster_set_option, "fdb_cluster_set_option");
				lib.Bind(ref Stubs.fdb_future_get_cluster, "fdb_future_get_cluster");

				lib.Bind(ref Stubs.fdb_cluster_create_database, "fdb_cluster_create_database");
				lib.Bind(ref Stubs.fdb_database_destroy, "fdb_database_destroy");
				lib.Bind(ref Stubs.fdb_future_get_database, "fdb_future_get_database");

				lib.Bind(ref Stubs.fdb_database_create_transaction, "fdb_database_create_transaction");
				lib.Bind(ref Stubs.fdb_transaction_set, "fdb_transaction_set");
				lib.Bind(ref Stubs.fdb_transaction_clear, "fdb_transaction_clear");
				lib.Bind(ref Stubs.fdb_transaction_commit, "fdb_transaction_commit");
				lib.Bind(ref Stubs.fdb_transaction_get_read_version, "fdb_transaction_get_read_version");
				lib.Bind(ref Stubs.fdb_transaction_get_committed_version, "fdb_transaction_get_committed_version");
				lib.Bind(ref Stubs.fdb_future_get_version, "fdb_future_get_version");
				lib.Bind(ref Stubs.fdb_transaction_get, "fdb_transaction_get");
				lib.Bind(ref Stubs.fdb_transaction_get_key, "fdb_transaction_get_key");
				lib.Bind(ref Stubs.fdb_future_get_key, "fdb_future_get_key");
				lib.Bind(ref Stubs.fdb_future_get_value, "fdb_future_get_value");
				lib.Bind(ref Stubs.fdb_transaction_get_range, "fdb_transaction_get_range");
			}

		}

		static FdbNative()
		{
			try
			{
				FdbCLib = UnmanagedLibrary.LoadLibrary(
					Path.Combine(Fdb.NativeLibPath, DLL_X86),
					Path.Combine(Fdb.NativeLibPath, DLL_X64)
				);

				Stubs.LoadBindings(FdbCLib);

			}
			catch (Exception e)
			{
				if (FdbCLib != null) FdbCLib.Dispose();
				FdbCLib = null;
				LibraryLoadError = e;
			}
		}

		public static bool IsLoaded
		{
			get { return LibraryLoadError == null && FdbCLib != null; }
		}

		private static void EnsureLibraryIsLoaded()
		{
			// should be inlined
			if (LibraryLoadError != null || FdbCLib == null) FailLibraryDidNotLoad();
		}

		private static void FailLibraryDidNotLoad()
		{
			throw new InvalidOperationException("An error occured while loading native FoundationDB library", LibraryLoadError);
		}

		private static string ToManagedString(byte* nativeString)
		{
			if (nativeString == null) return null;
			return Marshal.PtrToStringAnsi(new IntPtr((void*)nativeString));
		}

		private static string ToManagedString(IntPtr nativeString)
		{
			if (nativeString == IntPtr.Zero) return null;
			return Marshal.PtrToStringAnsi(nativeString);
		}

		internal static byte[] ToNativeString(string value, bool nullTerminated)
		{
			if (value == null) return null;

			byte[] result;
			if (nullTerminated)
			{ // NULL terminated ANSI string
				result = new byte[value.Length + 1];
				Encoding.Default.GetBytes(value, 0, value.Length, result, 0);
			}
			else
			{
				result = Encoding.Default.GetBytes(value);
			}
			return result;
		}

		#region Core..

		/// <summary>fdb_get_error</summary>
		public static string GetError(FdbError code)
		{
			EnsureLibraryIsLoaded();
			return ToManagedString(Stubs.fdb_get_error(code));
		}

		/// <summary>fdb_select_api_impl</summary>
		public static FdbError SelectApiVersionImpl(int runtimeVersion, int headerVersion)
		{
			EnsureLibraryIsLoaded();
			return Stubs.fdb_select_api_version_impl(runtimeVersion, headerVersion);
		}

		/// <summary>fdb_select_api_impl</summary>
		public static FdbError SelectApiVersion(int version)
		{
			return SelectApiVersionImpl(version, FDB_API_VERSION);
		}

		/// <summary>fdb_get_max_api_version</summary>
		public static int GetMaxApiVersion()
		{
			EnsureLibraryIsLoaded();
			return Stubs.fdb_get_max_api_version();
		}

		#endregion

		#region Futures...

		public static bool FutureIsReady(FutureHandle futureHandle)
		{
			EnsureLibraryIsLoaded();
			return Stubs.fdb_future_is_ready(futureHandle);
		}

		public static void FutureDestroy(IntPtr futureHandle)
		{
			EnsureLibraryIsLoaded();
			Stubs.fdb_future_destroy(futureHandle);
		}

		public static bool FutureIsError(FutureHandle futureHandle)
		{
			EnsureLibraryIsLoaded();
			return Stubs.fdb_future_is_error(futureHandle);
		}

		/// <summary>Return the error got from a FDBFuture</summary>
		/// <param name="futureHandle"></param>
		/// <returns></returns>
		public static FdbError FutureGetError(FutureHandle future)
		{
			EnsureLibraryIsLoaded();
			return Stubs.fdb_future_get_error(future, null);
		}

		public static FdbError FutureGetError(FutureHandle future, out string description)
		{
			EnsureLibraryIsLoaded();

			byte* ptr = null;
			var err = Stubs.fdb_future_get_error(future, &ptr);
			description = ToManagedString(ptr);
			return err;
		}

		public static FdbError FutureBlockUntilReady(FutureHandle future)
		{
			EnsureLibraryIsLoaded();

			Debug.WriteLine("calling fdb_future_block_until_ready(0x" + future.Handle.ToString("x") + ")...");
			var err = Stubs.fdb_future_block_until_ready(future);
			Debug.WriteLine("fdb_future_block_until_ready(0x" + future.Handle.ToString("x") + ") => err=" + err);
			return err;
		}

		public static FdbError FutureSetCallback(FutureHandle future, FdbFutureCallback callback, IntPtr callbackParameter)
		{
			EnsureLibraryIsLoaded();
			var ptrCallback = Marshal.GetFunctionPointerForDelegate(callback);
			var err = Stubs.fdb_future_set_callback(future, ptrCallback, callbackParameter);
			Debug.WriteLine("fdb_future_set_callback(0x" + future.Handle.ToString("x") + ", 0x" + ptrCallback.ToString("x") + ") => err=" + err);
			return err;
		}

		#endregion

		#region Network...

		public static FdbError NetworkSetOption(FdbNetworkOption option, byte* value, int valueLength)
		{
			EnsureLibraryIsLoaded();
			return Stubs.fdb_network_set_option(option, value, valueLength);
		}

		public static FdbError SetupNetwork()
		{
			return Stubs.fdb_setup_network();
		}

		public static FdbError RunNetwork()
		{
			return Stubs.fdb_run_network();
		}

		public static FdbError StopNetwork()
		{
			return Stubs.fdb_stop_network();
		}

		#endregion

		#region Clusters...

		public static FutureHandle CreateCluster(string path)
		{
			EnsureLibraryIsLoaded();

			var data = ToNativeString(path, nullTerminated: true);
			fixed (byte* ptr = data)
			{
				var future = new FutureHandle();
				var handle = Stubs.fdb_create_cluster(ptr);
				Debug.WriteLine("fdb_create_cluster(" + path + ") => 0x" + handle.ToString("x"));
				future.TrySetHandle(handle);
				return future;
			}
		}

		public static void ClusterDestroy(IntPtr handle)
		{
			EnsureLibraryIsLoaded();
			RuntimeHelpers.PrepareConstrainedRegions();
			try { }
			finally
			{
				Stubs.fdb_cluster_destroy(handle);
			}
		}

		public static FdbError FutureGetCluster(FutureHandle future, out ClusterHandle cluster)
		{
			EnsureLibraryIsLoaded();
			cluster = new ClusterHandle();
			FdbError err;

			RuntimeHelpers.PrepareConstrainedRegions();
			try { }
			finally
			{
				IntPtr handle;
				err = Stubs.fdb_future_get_cluster(future, out handle);
				Debug.WriteLine("fdb_future_get_cluster(0x" + future.Handle.ToString("x") + ") => err=" + err + ", handle=0x" + handle.ToString("x"));
				//TODO: check is err == Success ?
				cluster.TrySetHandle(handle);
			}
			return err;
		}

		#endregion

		#region Databases...

		public static FdbError FutureGetDatabase(FutureHandle future, out DatabaseHandle database)
		{
			EnsureLibraryIsLoaded();

			database = new DatabaseHandle();
			FdbError err;

			RuntimeHelpers.PrepareConstrainedRegions();
			try { }
			finally
			{
				IntPtr handle;
				err = Stubs.fdb_future_get_database(future.Handle, out handle);
				//TODO: check is err == Success ?
				database.TrySetHandle(handle);
			}
			return err;
		}

		public static void DatabaseDestroy(IntPtr handle)
		{
			EnsureLibraryIsLoaded();

			RuntimeHelpers.PrepareConstrainedRegions();
			try { }
			finally
			{
				Stubs.fdb_database_destroy(handle);
			}
		}

		public static FutureHandle ClusterCreateDatabase(ClusterHandle cluster, string name)
		{
			EnsureLibraryIsLoaded();

			var data = ToNativeString(name, nullTerminated: false);
			fixed (byte* ptr = data)
			{
				var future = new FutureHandle();

				RuntimeHelpers.PrepareConstrainedRegions();
				try { }
				finally
				{
					var handle = Stubs.fdb_cluster_create_database(cluster, ptr, data == null ? 0 : data.Length);
					Debug.WriteLine("fdb_cluster_create_database(0x" + cluster.Handle.ToString("x") + ", '" + name + "') => 0x" + handle.ToString("x"));
					future.TrySetHandle(handle);
				}
				return future;
			}
		}

		#endregion

		#region Transactions...

		public static void TransactionDestroy(IntPtr handle)
		{
			EnsureLibraryIsLoaded();

			RuntimeHelpers.PrepareConstrainedRegions();
			try { }
			finally
			{
				Stubs.fdb_database_destroy(handle);
			}
		}

		public static FdbError DatabaseCreateTransaction(DatabaseHandle database, out TransactionHandle transaction)
		{
			EnsureLibraryIsLoaded();
			transaction = new TransactionHandle();
			FdbError err;

			RuntimeHelpers.PrepareConstrainedRegions();
			try { }
			finally
			{
				IntPtr handle;
				err = Stubs.fdb_database_create_transaction(database, out handle);
				Debug.WriteLine("fdb_database_create_transaction(0x" + database.Handle.ToString("x") + ") => err=" + err + ", handle=0x" + handle.ToString("x"));
				transaction.TrySetHandle(handle);
			}
			return err;

		}

		public static FutureHandle TransactionCommit(TransactionHandle transaction)
		{
			EnsureLibraryIsLoaded();
			var future = new FutureHandle();

			RuntimeHelpers.PrepareConstrainedRegions();
			try { }
			finally
			{
				var handle = Stubs.fdb_transaction_commit(transaction);
				Debug.WriteLine("fdb_transaction_commit(0x" + transaction.Handle.ToString("x") + ") => 0x" + handle.ToString("x"));
				future.TrySetHandle(handle);
			}
			return future;
		}

		public static FutureHandle TransactionGetReadVersion(TransactionHandle transaction)
		{
			EnsureLibraryIsLoaded();
			var future = new FutureHandle();

			RuntimeHelpers.PrepareConstrainedRegions();
			try { }
			finally
			{
				var handle = Stubs.fdb_transaction_get_read_version(transaction);
				Debug.WriteLine("fdb_transaction_get_read_version(0x" + transaction.Handle.ToString("x") + ") => 0x" + handle.ToString("x"));
				future.TrySetHandle(handle);
			}
			return future;
		}

		public static FdbError TransactionGetCommittedVersion(TransactionHandle transaction, out long version)
		{
			EnsureLibraryIsLoaded();

			return Stubs.fdb_transaction_get_committed_version(transaction, out version);
		}

		public static FdbError FutureGetVersion(FutureHandle future, out long version)
		{
			EnsureLibraryIsLoaded();

			return Stubs.fdb_future_get_version(future, out version);
		}

		public static FutureHandle TransactionGet(TransactionHandle transaction, ArraySegment<byte> key, bool snapshot)
		{
			EnsureLibraryIsLoaded();

			var future = new FutureHandle();

			RuntimeHelpers.PrepareConstrainedRegions();
			try { }
			finally
			{
				fixed (byte* ptrKey = key.Array)
				{
					var handle = Stubs.fdb_transaction_get(transaction, ptrKey + key.Offset, key.Count, snapshot);
					Debug.WriteLine("fdb_transaction_get(0x" + transaction.Handle.ToString("x") + ", [" + key.Count + "], " + snapshot + ") => 0x" + handle.ToString("x"));
					future.TrySetHandle(handle);
				}
			}
			return future;
		}

		public static FutureHandle TransactionGetKey(TransactionHandle transaction, byte[] keyName, int keyLength, bool orEqual, int offset, bool snapshot)
		{
			EnsureLibraryIsLoaded();
			if (keyName == null) throw new ArgumentNullException("keyName");
			if (keyName.Length < keyLength) throw new ArgumentOutOfRangeException("keyLength");

			var future = new FutureHandle();

			RuntimeHelpers.PrepareConstrainedRegions();
			try { }
			finally
			{
				fixed (byte* ptrKey = keyName)
				{
					var handle = Stubs.fdb_transaction_get_key(transaction, ptrKey, keyLength, orEqual, offset, snapshot);
					Debug.WriteLine("fdb_transaction_get_key(0x" + transaction.Handle.ToString("x") + ", [" + keyLength + "], " + orEqual + ", " + offset + ", " + snapshot + ") => 0x" + handle.ToString("x"));
					future.TrySetHandle(handle);
				}
			}
			return future;
		}

		public static FdbError FutureGetValue(FutureHandle future, out bool valuePresent, out byte[] value, out int valueLength)
		{
			EnsureLibraryIsLoaded();

			valuePresent = false;
			value = null;
			valueLength = 0;

			byte* ptr = null;
			var err = Stubs.fdb_future_get_value(future, out valuePresent, out ptr, out valueLength);
			Debug.WriteLine("fdb_future_get_value(0x" + future.Handle.ToString("x") + ") => err=" + err + ", present=" + valuePresent + ", valueLength=" + valueLength);
			if (ptr != null && valueLength >= 0)
			{
				value = new byte[valueLength];
				Marshal.Copy(new IntPtr(ptr), value, 0, valueLength);
			}

			return err;
		}

		public static FdbError FutureGetKey(FutureHandle future, out byte[] key, out int keyLength)
		{
			EnsureLibraryIsLoaded();

			key = null;
			keyLength = 0;

			byte* ptr = null;
			var err = Stubs.fdb_future_get_key(future, out ptr, out keyLength);
			if (ptr != null && keyLength >= 0)
			{
				key = new byte[keyLength];
				Marshal.Copy(new IntPtr(ptr), key, 0, keyLength);
			}

			return err;
		}

		public static void TransactionSet(TransactionHandle transaction, ArraySegment<byte> key, ArraySegment<byte> value)
		{
			EnsureLibraryIsLoaded();

			fixed (byte* pKey = key.Array)
			fixed (byte* pValue = value.Array)
			{
				Debug.WriteLine("fdb_transaction_set(0x" + transaction.Handle.ToString("x") + ", [" + key.Count + "], [" + value.Count + "])");
				Stubs.fdb_transaction_set(transaction, pKey + key.Offset, key.Count, pValue + value.Offset, value.Count);
			}
		}

		public static void TransactionClear(TransactionHandle transaction, ArraySegment<byte> key)
		{
			EnsureLibraryIsLoaded();

			fixed (byte* pKey = key.Array)
			{
				Debug.WriteLine("fdb_transaction_clear(0x" + transaction.Handle.ToString("x") + ", [" + key.Count + "])");
				Stubs.fdb_transaction_clear(transaction, pKey + key.Offset, key.Count);
			}
		}

		#endregion

	}

}
