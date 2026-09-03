#region License
/* FNA - XNA4 Reimplementation for Desktop Platforms
 * Copyright 2009-2024 Ethan Lee and the MonoGame Team
 *
 * Released under the Microsoft Public License.
 * See LICENSE for details.
 */

/* Derived from code by the Mono.Xna Team (Copyright 2006).
 * Released under the MIT License. See monoxna.LICENSE for details.
 */
#endregion

#region Using Statements
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Microsoft.Xna.Framework.Content
{
	class VertexBufferReader : ContentTypeReader<VertexBuffer>
	{
		#region Protected Read Method

		protected internal override VertexBuffer Read(
			ContentReader input,
			VertexBuffer existingInstance
		) {
			if (input.version < 5)
			{
				/* An XNB v4 vertex buffer is a bag of bytes and nothing else —
				 *
				 *     int32 sizeInBytes
				 *     byte[sizeInBytes]
				 *
				 * (VertexBufferReader::Read in the 3.1 redistributable). 3.1's VertexBuffer had no
				 * declaration attached; the declaration lived on the ModelMeshPart, and the model
				 * stored its declarations in a separate up-front array that the parts index into.
				 * FNA's VertexBuffer cannot exist without a declaration, so the bytes are handed
				 * to the v4 ModelReader, which owns that array and builds the buffer once it has
				 * read the part that names the declaration.
				 */
				if (!input.xna31ExpectsRawVertexBuffer)
				{
					throw new ContentLoadException(
						"An XNB v4 VertexBuffer can only be read as part of a Model: it carries no " +
						"vertex declaration of its own."
					);
				}
				input.xna31RawVertexBuffer = input.ReadBytes(input.ReadInt32());
				return null;
			}

			VertexDeclaration declaration = input.ReadRawObject<VertexDeclaration>();
			int vertexCount = (int) input.ReadUInt32();
			byte[] data = input.ReadBytes(vertexCount * declaration.VertexStride);

			VertexBuffer buffer = new VertexBuffer(
				input.ContentManager.GetGraphicsDevice(),
				declaration,
				vertexCount,
				BufferUsage.None
			);
			buffer.SetData(data);
			return buffer;
		}

		#endregion
	}
}
