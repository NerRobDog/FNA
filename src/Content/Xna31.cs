#region License
/* FNA - XNA4 Reimplementation for Desktop Platforms
 * Copyright 2009-2024 Ethan Lee and the MonoGame Team
 *
 * Released under the Microsoft Public License.
 * See LICENSE for details.
 */
#endregion

/* XNA 3.1 (XNB version 4) support.
 *
 * This file is the whole of the v4 delta that is not a `reader.version < 5` branch inside an
 * individual reader: the enum translation tables, the reader-name normalisation, and the hook a
 * host uses to resolve content readers that live in ITS assembly load context rather than FNA's.
 *
 * Every number below was read out of the real XNA 3.1 redistributable
 * (Microsoft.Xna.Framework.dll, 3.1.0.0__6d5c3888ef60e27d) with ikdasm. None of it is guessed, and
 * none of it comes from any game.
 *
 * Two things make 3.1 content different from 4.0 content:
 *
 *   1. Its enums are D3D9's, not XNA 4's. SurfaceFormat, VertexElementFormat and
 *      VertexElementUsage all renumbered between 3.1 and 4.0, and 3.1 has members 4.0 dropped
 *      (D3DFMT_A2W10V10W10 and friends).
 *   2. Several payloads are laid out differently, because 3.1's runtime objects were shaped
 *      differently — a 3.1 VertexBuffer is a bag of bytes with no declaration attached, and a 3.1
 *      VertexElement carries a stream index and a tessellator method that 4.0 has no room for.
 *      Those deltas live in the readers themselves.
 */

#region Using Statements
using System;
using System.Collections.Generic;
using System.Reflection;

using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Microsoft.Xna.Framework.Content
{
	/// <summary>
	/// XNA 3.1 / XNB version 4 compatibility: enum translation and type-name resolution.
	/// </summary>
	/// <remarks>
	/// The tables below are complete against the 3.1 enums, not against one title. For reference,
	/// a scan of all 4308 XNBs Magicka ships (tools/golden/XnbGolden --survey) finds only:
	/// surface formats 1 (Color/BGRA), 28 (Dxt1) and 32 (Dxt5); vertex element formats 1-5
	/// (Vector2, Vector3, Vector4, Color, Byte4) — none of the packed 10:10:10 types, so the
	/// PHASE3-BOUNDARY below is unreached by that content; vertex element usages 0, 1, 2, 3, 5 and
	/// 10, every one of which renumbered between 3.1 and 4.0; and stream index and tessellator
	/// method 0 throughout.
	/// </remarks>
	public static class Xna31
	{
		#region Reader Type Resolution Hook

		/// <summary>
		/// Consulted by <see cref="ContentTypeReaderManager"/> when a content reader named in an XNB
		/// cannot be found by <see cref="Type.GetType(string)"/>.
		/// </summary>
		/// <remarks>
		/// <para>XNA 3.1 XNBs name their custom readers with a PARTIAL assembly name — Magicka's, for
		/// instance, are written as <c>Magicka.ContentReaders.ItemReader, Magicka</c> or
		/// <c>..., Magicka, Version=1.0.0.0, Culture=neutral</c>, with no public key token. .NET
		/// Framework resolved partial names by probing the app base; .NET Core does not, and in any
		/// case a host that loads the game's assemblies into its own
		/// <c>AssemblyLoadContext</c> is invisible to <c>Type.GetType</c>, which only ever looks in
		/// the load context of the calling assembly (this one).</para>
		/// <para>So the host installs a resolver. It is handed the reader-type string exactly as the
		/// XNB spells it and returns the <see cref="Type"/> to instantiate, or null to let the
		/// normal "could not find" error happen.</para>
		/// </remarks>
		public static Func<string, Type> ReaderTypeResolver
		{
			get;
			set;
		}

		/// <summary>
		/// Replaces the content reader FNA would otherwise use for <paramref name="readerTypeString"/>
		/// with one the host supplies. The name must be spelled exactly as the XNB spells it (for the
		/// builtins that is the bare type name, e.g.
		/// <c>Microsoft.Xna.Framework.Content.ModelReader</c>).
		/// </summary>
		/// <remarks>
		/// <para>This exists for a host that reimplements XNA 3.1's OWN object model rather than
		/// using FNA's. Such a host's <c>Model</c>, <c>VertexDeclaration</c> and buffer types are
		/// its own types, so a reader that produces FNA's cannot serve it — the cast on the way out
		/// of <c>ReadObject&lt;T&gt;</c> would throw <c>InvalidCastException</c> naming two
		/// identically-spelled types from different assemblies.</para>
		/// <para>Overrides win over <see cref="Type.GetType"/> and over
		/// <see cref="ReaderTypeResolver"/>, and the first registration for a name wins (matching
		/// the type-creator table this delegates to). Registering one does not change any payload
		/// FNA reads for itself; it changes only which reader instance is handed the stream.</para>
		/// </remarks>
		public static void AddReaderOverride(
			string readerTypeString,
			Func<ContentTypeReader> createReader
		) {
			if (string.IsNullOrEmpty(readerTypeString))
			{
				throw new ArgumentNullException("readerTypeString");
			}
			if (createReader == null)
			{
				throw new ArgumentNullException("createReader");
			}
			ContentTypeReaderManager.AddTypeCreator(readerTypeString, createReader);
		}

		#endregion

		#region Reader Type Name Normalisation

		/// <summary>
		/// Rewrites the mscorlib 2.0 assembly references XNA 3.1 baked into generic reader names
		/// (<c>DictionaryReader`2[[System.String, mscorlib, Version=2.0.0.0, ...]]</c>) to the corlib
		/// this runtime actually has.
		/// </summary>
		/// <remarks>
		/// The 3.1 references to <c>Microsoft.Xna.Framework</c> itself are handled by
		/// <c>ContentTypeReaderManager.PrepareType</c>, whose regex already covers them; this fills
		/// the other half. .NET's default context will usually unify an mscorlib 2.0.0.0 reference
		/// onto the shipped facade on its own, but "usually" is not a contract, and a name that is
		/// explicit about the corlib it means resolves the same way on every runtime.
		/// </remarks>
		public static string NormalizeCorlibReferences(string type)
		{
			const string legacy = ", mscorlib, Version=2.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089";
			if (type.IndexOf(legacy, StringComparison.Ordinal) < 0)
			{
				return type;
			}
			return type.Replace(legacy, ", " + typeof(object).Assembly.FullName);
		}

		#endregion

		#region SurfaceFormat

		/* XNA 3.1's SurfaceFormat is D3DFORMAT with names on it, so its members describe CHANNEL
		 * ORDER IN MEMORY the D3D9 way (little-endian ARGB words). XNA 4 renumbered from scratch and
		 * kept only what every target could do. `Color` is the trap: 3.1's Color == 1 == D3DFMT_
		 * A8R8G8B8, which is BGRA in memory, while FNA's SurfaceFormat.Color is RGBA. They are not
		 * the same format and mapping one to the other swaps red and blue in every 3.1 texture.
		 */
		private static readonly Dictionary<int, SurfaceFormat> surfaceFormats = new Dictionary<int, SurfaceFormat>
		{
			{ 1,  SurfaceFormat.ColorBgraEXT },	// Color         (D3DFMT_A8R8G8B8)
			{ 2,  SurfaceFormat.ColorBgraEXT },	// Bgr32         (D3DFMT_X8R8G8B8, alpha ignored)
			{ 3,  SurfaceFormat.Rgba1010102 },	// Bgra1010102   (see note below)
			{ 4,  SurfaceFormat.Color },		// Rgba32        (D3DFMT_A8B8G8R8)
			{ 5,  SurfaceFormat.Color },		// Rgb32         (D3DFMT_X8B8G8R8, alpha ignored)
			{ 6,  SurfaceFormat.Rgba1010102 },	// Rgba1010102   (D3DFMT_A2B10G10R10)
			{ 7,  SurfaceFormat.Rg32 },		// Rg32          (D3DFMT_G16R16)
			{ 8,  SurfaceFormat.Rgba64 },		// Rgba64        (D3DFMT_A16B16G16R16)
			{ 9,  SurfaceFormat.Bgr565 },		// Bgr565        (D3DFMT_R5G6B5)
			{ 10, SurfaceFormat.Bgra5551 },		// Bgra5551      (D3DFMT_A1R5G5B5)
			{ 12, SurfaceFormat.Bgra4444 },		// Bgra4444      (D3DFMT_A4R4G4B4)
			{ 15, SurfaceFormat.Alpha8 },		// Alpha8        (D3DFMT_A8)
			{ 22, SurfaceFormat.Single },		// Single        (D3DFMT_R32F)
			{ 23, SurfaceFormat.Vector2 },		// Vector2       (D3DFMT_G32R32F)
			{ 24, SurfaceFormat.Vector4 },		// Vector4       (D3DFMT_A32B32G32R32F)
			{ 25, SurfaceFormat.HalfSingle },	// HalfSingle    (D3DFMT_R16F)
			{ 26, SurfaceFormat.HalfVector2 },	// HalfVector2   (D3DFMT_G16R16F)
			{ 27, SurfaceFormat.HalfVector4 },	// HalfVector4   (D3DFMT_A16B16G16R16F)
			{ 28, SurfaceFormat.Dxt1 },		// Dxt1
			{ 30, SurfaceFormat.Dxt3 },		// Dxt3
			{ 32, SurfaceFormat.Dxt5 },		// Dxt5
			{ 33, SurfaceFormat.Alpha8 },		// Luminance8    (D3DFMT_L8; single 8-bit channel)
		};

		/* Note on 3 -> Rgba1010102: 3.1's Bgra1010102 is D3DFMT_A2R10G10B10, whose red and blue are
		 * the other way round from FNA's Rgba1010102 (D3DFMT_A2B10G10R10). FNA has no BGR 10:10:10:2
		 * format, so this mapping keeps the bit widths and loses the channel order. It is here so a
		 * texture in that format loads at all rather than throwing; if one ever turns up in real
		 * content it will look wrong and want a swizzle on the level data. Magicka ships none.
		 */

		/// <summary>Translates an XNA 3.1 SurfaceFormat value to its FNA counterpart.</summary>
		/// <exception cref="ContentLoadException">
		/// The format is one of the 3.1 members with no FNA equivalent at all (the palettised,
		/// video, multi-element and depth-buffer formats). None appear in shipped 3.1 texture
		/// content, so this is a "the XNB is not what we think it is" signal.
		/// </exception>
		public static SurfaceFormat TranslateSurfaceFormat(int legacyFormat)
		{
			SurfaceFormat format;
			if (surfaceFormats.TryGetValue(legacyFormat, out format))
			{
				return format;
			}
			throw new ContentLoadException(
				"XNB v4 surface format " + legacyFormat + " has no FNA equivalent."
			);
		}

		#endregion

		#region VertexElementFormat

		/* 3.1 and 4.0 agree on 0-7 (Single..Short4) and then diverge, because 3.1 exposed the whole
		 * of D3DDECLTYPE and 4.0 kept the subset every platform implements.
		 */
		private const int Xna31Rgba32 = 8;		// D3DDECLTYPE_UBYTE4N
		private const int Xna31NormalizedShort2 = 9;	// D3DDECLTYPE_SHORT2N
		private const int Xna31NormalizedShort4 = 10;	// D3DDECLTYPE_SHORT4N
		private const int Xna31Rg32 = 11;		// D3DDECLTYPE_USHORT2N
		private const int Xna31Rgba64 = 12;		// D3DDECLTYPE_USHORT4N
		private const int Xna31UInt101010 = 13;		// D3DDECLTYPE_UDEC3
		private const int Xna31Normalized101010 = 14;	// D3DDECLTYPE_DEC3N
		private const int Xna31HalfVector2 = 15;	// D3DDECLTYPE_FLOAT16_2
		private const int Xna31HalfVector4 = 16;	// D3DDECLTYPE_FLOAT16_4
		private const int Xna31Unused = 17;		// D3DDECLTYPE_UNUSED

		/// <summary>
		/// The 3.1 vertex element formats that FNA cannot express, and whose vertex data therefore
		/// has to be rewritten at load before the buffer is usable.
		/// </summary>
		/// <remarks>
		/// PHASE3-BOUNDARY. Both are the packed 10:10:10 D3D9 types. Translating them costs a pass
		/// over the vertex data (unpack 3x10 bits, widen), which is a buffer-rewriting job rather
		/// than a table lookup; until that lands, a declaration containing one keeps the widest
		/// same-size format so the stride stays right and the geometry loads, and the element is
		/// reported here so the caller can tell it is not renderable as-is.
		/// </remarks>
		public static bool IsUnsupportedVertexElementFormat(int legacyFormat)
		{
			return legacyFormat == Xna31UInt101010 || legacyFormat == Xna31Normalized101010;
		}

		/// <summary>Translates an XNA 3.1 VertexElementFormat value to its FNA counterpart.</summary>
		public static VertexElementFormat TranslateVertexElementFormat(int legacyFormat)
		{
			if (legacyFormat <= (int) VertexElementFormat.Short4)
			{
				// Single, Vector2, Vector3, Vector4, Color, Byte4, Short2, Short4 — same numbers.
				return (VertexElementFormat) legacyFormat;
			}

			switch (legacyFormat)
			{
				case Xna31Rgba32:
					/* UBYTE4N: four normalised bytes, RGBA order. FNA's Color is the same four
					 * normalised bytes; only the channel order differs, and the shader decides
					 * what the channels mean, so the stride and the fetch are both correct.
					 */
					return VertexElementFormat.Color;

				case Xna31NormalizedShort2:
					return VertexElementFormat.NormalizedShort2;

				case Xna31NormalizedShort4:
					return VertexElementFormat.NormalizedShort4;

				case Xna31Rg32:
					// USHORT2N: unsigned where FNA's is signed, same two 16-bit lanes.
					return VertexElementFormat.NormalizedShort2;

				case Xna31Rgba64:
					// USHORT4N: unsigned where FNA's is signed, same four 16-bit lanes.
					return VertexElementFormat.NormalizedShort4;

				case Xna31UInt101010:
				case Xna31Normalized101010:
					/* PHASE3-BOUNDARY, see IsUnsupportedVertexElementFormat. Four bytes in, four
					 * bytes out, so the declaration's stride survives and everything after this
					 * element stays at the right offset.
					 */
					return VertexElementFormat.Color;

				case Xna31HalfVector2:
					return VertexElementFormat.HalfVector2;

				case Xna31HalfVector4:
					return VertexElementFormat.HalfVector4;

				case Xna31Unused:
					throw new ContentLoadException(
						"XNB v4 vertex element format Unused cannot be part of a declaration."
					);
			}

			throw new ContentLoadException(
				"Unknown XNB v4 vertex element format " + legacyFormat + "."
			);
		}

		#endregion

		#region VertexElementUsage

		/* Completely renumbered between 3.1 and 4.0 — there is no arithmetic relation, so this is a
		 * table. 3.1 order: Position, BlendWeight, BlendIndices, Normal, PointSize,
		 * TextureCoordinate, Tangent, Binormal, TessellateFactor, (9 unused), Color, Fog, Depth,
		 * Sample. This is D3DDECLUSAGE.
		 */
		private static readonly VertexElementUsage[] vertexElementUsages =
		{
			/*  0 */ VertexElementUsage.Position,
			/*  1 */ VertexElementUsage.BlendWeight,
			/*  2 */ VertexElementUsage.BlendIndices,
			/*  3 */ VertexElementUsage.Normal,
			/*  4 */ VertexElementUsage.PointSize,
			/*  5 */ VertexElementUsage.TextureCoordinate,
			/*  6 */ VertexElementUsage.Tangent,
			/*  7 */ VertexElementUsage.Binormal,
			/*  8 */ VertexElementUsage.TessellateFactor,
			/*  9 */ (VertexElementUsage) (-1),	// D3DDECLUSAGE_POSITIONT, not in either XNA
			/* 10 */ VertexElementUsage.Color,
			/* 11 */ VertexElementUsage.Fog,
			/* 12 */ VertexElementUsage.Depth,
			/* 13 */ VertexElementUsage.Sample,
		};

		/// <summary>Translates an XNA 3.1 VertexElementUsage value to its FNA counterpart.</summary>
		public static VertexElementUsage TranslateVertexElementUsage(int legacyUsage)
		{
			if (	legacyUsage < 0 ||
				legacyUsage >= vertexElementUsages.Length ||
				(int) vertexElementUsages[legacyUsage] < 0	)
			{
				throw new ContentLoadException(
					"Unknown XNB v4 vertex element usage " + legacyUsage + "."
				);
			}
			return vertexElementUsages[legacyUsage];
		}

		#endregion

		#region VertexElementFormat Sizes

		/// <summary>
		/// Size in bytes of an XNA 3.1 vertex element format, as D3D9 defines it. Needed because a
		/// v4 vertex declaration does not carry its own stride — 3.1 computed it from the elements.
		/// </summary>
		public static int VertexElementSize(int legacyFormat)
		{
			switch (legacyFormat)
			{
				case 0: return 4;		// Single
				case 1: return 8;		// Vector2
				case 2: return 12;		// Vector3
				case 3: return 16;		// Vector4
				case 4: return 4;		// Color
				case 5: return 4;		// Byte4
				case 6: return 4;		// Short2
				case 7: return 8;		// Short4
				case Xna31Rgba32: return 4;
				case Xna31NormalizedShort2: return 4;
				case Xna31NormalizedShort4: return 8;
				case Xna31Rg32: return 4;
				case Xna31Rgba64: return 8;
				case Xna31UInt101010: return 4;
				case Xna31Normalized101010: return 4;
				case Xna31HalfVector2: return 4;
				case Xna31HalfVector4: return 8;
			}
			throw new ContentLoadException(
				"Unknown XNB v4 vertex element format " + legacyFormat + "."
			);
		}

		#endregion
	}
}
