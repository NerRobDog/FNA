#region License
/* FNA - XNA4 Reimplementation for Desktop Platforms
 * Copyright 2009-2024 Ethan Lee and the MonoGame Team
 *
 * Released under the Microsoft Public License.
 * See LICENSE for details.
 */
#endregion

#region Using Statements
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Microsoft.Xna.Framework.Content
{
	internal class VertexDeclarationReader : ContentTypeReader<VertexDeclaration>
	{
		#region Protected Read Method

		protected internal override VertexDeclaration Read(
			ContentReader reader,
			VertexDeclaration existingInstance
		) {
			if (reader.version < 5)
			{
				return ReadXna31(reader);
			}

			int vertexStride = reader.ReadInt32();
			int elementCount = reader.ReadInt32();
			VertexElement[] elements = new VertexElement[elementCount];
			for (int i = 0; i < elementCount; i += 1)
			{
				int offset = reader.ReadInt32();
				VertexElementFormat elementFormat = (VertexElementFormat) reader.ReadInt32();
				VertexElementUsage elementUsage = (VertexElementUsage) reader.ReadInt32();
				int usageIndex = reader.ReadInt32();
				elements[i] = new VertexElement(
					offset,
					elementFormat,
					elementUsage,
					usageIndex
				);
			}

			/* TODO: This process generates alot of duplicate VertexDeclarations
			 * which in turn complicates other systems trying to share GPU resources
			 * like DX11 vertex input layouts.
			 *
			 * We should consider caching vertex declarations here and returning
			 * previously created declarations when they are in our cache.
			 */
			return new VertexDeclaration(vertexStride, elements);
		}

		#endregion

		#region XNA 3.1 Read Method

		/* An XNB v4 vertex declaration is a raw D3D9 vertex declaration: no stride (3.1 computed it
		 * from the elements), and each element is eight bytes rather than sixteen —
		 *
		 *     int32 elementCount
		 *     per element:
		 *         int16 stream
		 *         int16 offset
		 *         byte  format   (D3DDECLTYPE, 3.1's numbering)
		 *         byte  method   (D3DDECLMETHOD)
		 *         byte  usage    (D3DDECLUSAGE, 3.1's numbering)
		 *         byte  usageIndex
		 *
		 * — taken from VertexDeclarationReader::Read in the 3.1 redistributable. FNA has no stream
		 * index (XNA 4 moved multi-stream to SetVertexBuffers) and no tessellator method, so both
		 * are dropped; every declaration Magicka ships uses stream 0 and method Default.
		 */
		private static VertexDeclaration ReadXna31(ContentReader reader)
		{
			int elementCount = reader.ReadInt32();
			VertexElement[] elements = new VertexElement[elementCount];
			int vertexStride = 0;

			for (int i = 0; i < elementCount; i += 1)
			{
				short stream = reader.ReadInt16();
				short offset = reader.ReadInt16();
				byte format = reader.ReadByte();
				byte method = reader.ReadByte();
				byte usage = reader.ReadByte();
				byte usageIndex = reader.ReadByte();

				if (stream != 0)
				{
					throw new ContentLoadException(
						"XNB v4 vertex declaration uses stream " + stream +
						"; FNA vertex declarations are single-stream."
					);
				}
				if (method != 0)
				{
					throw new ContentLoadException(
						"XNB v4 vertex declaration uses tessellator method " + method +
						"; FNA has no equivalent."
					);
				}

				elements[i] = new VertexElement(
					offset,
					Xna31.TranslateVertexElementFormat(format),
					Xna31.TranslateVertexElementUsage(usage),
					usageIndex
				);

				/* 3.1 asked D3D for the stride; here it is the end of the furthest element, which
				 * is the same number for every declaration the content pipeline emits.
				 */
				int end = offset + Xna31.VertexElementSize(format);
				if (end > vertexStride)
				{
					vertexStride = end;
				}
			}

			return new VertexDeclaration(vertexStride, elements);
		}

		#endregion
	}
}

