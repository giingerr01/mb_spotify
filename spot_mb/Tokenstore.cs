using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MusicBeePlugin
{
	internal enum TokenLoadStatus
	{
		Ok,
		Missing,
		Corrupt,   // file exists but can't be decrypted (damaged, or saved by another Windows user)
		IoError    // file couldn't be read right now (locked, permissions); don't treat as corrupt
	}

	/// <summary>
	/// Persists a secret string (the Spotify token JSON) so it is not readable as plain text.
	/// Uses Windows DPAPI scoped to the current user, and writes atomically: the new data goes to a
	/// temp file first, then replaces the real file, so an interrupted save keeps the previous token.
	/// Has no WinForms / MusicBee / Spotify dependencies so it can be unit tested on its own.
	/// Requires a reference to System.Security (a .NET Framework assembly, nothing to embed).
	/// </summary>
	internal static class TokenStore
	{
		// Not a secret; just ties the encrypted blob to this plugin.
		private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("mb_Spotify_Plugin/token/v1");
		private static readonly object WriteLock = new object();

		public static void Save(string path, string plainText)
		{
			if (string.IsNullOrEmpty(path)) throw new ArgumentException("path is required", nameof(path));
			if (plainText == null) throw new ArgumentNullException(nameof(plainText));

			byte[] protectedBytes = ProtectedData.Protect(
				Encoding.UTF8.GetBytes(plainText), Entropy, DataProtectionScope.CurrentUser);

			string tempPath = path + ".tmp";

			lock (WriteLock)
			{
				try
				{
					using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
					{
						stream.Write(protectedBytes, 0, protectedBytes.Length);
						stream.Flush(true); // push to disk before swapping it in
					}

					if (File.Exists(path))
					{
						File.Replace(tempPath, path, null); // atomic swap, no backup file
					}
					else
					{
						File.Move(tempPath, path);
					}
				}
				catch
				{
					try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
					throw; // the existing token file (if any) is untouched
				}
			}
		}

		public static TokenLoadStatus TryLoad(string path, out string plainText, out string error)
		{
			plainText = null;
			error = null;

			try
			{
				// A leftover .tmp means a save was interrupted; the real file is still the last good one.
				string tempPath = path + ".tmp";
				if (File.Exists(tempPath))
				{
					try { File.Delete(tempPath); } catch { }
				}

				if (!File.Exists(path))
				{
					return TokenLoadStatus.Missing;
				}

				byte[] protectedBytes = File.ReadAllBytes(path);
				byte[] bytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
				plainText = Encoding.UTF8.GetString(bytes);
				return TokenLoadStatus.Ok;
			}
			catch (CryptographicException e)
			{
				error = e.GetType().Name + " - " + e.Message;
				return TokenLoadStatus.Corrupt;
			}
			catch (Exception e)
			{
				error = e.GetType().Name + " - " + e.Message;
				return TokenLoadStatus.IoError;
			}
		}

		/// <summary>Deletes the file and any leftover temp file. Returns false if something couldn't be removed.</summary>
		public static bool Delete(string path)
		{
			if (string.IsNullOrEmpty(path)) return true;

			bool ok = true;
			foreach (var p in new[] { path, path + ".tmp" })
			{
				try
				{
					if (File.Exists(p)) File.Delete(p);
				}
				catch
				{
					ok = false;
				}
			}
			return ok;
		}
	}
}