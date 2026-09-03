#region License
/* FNA - XNA4 Reimplementation for Desktop Platforms
 * Copyright 2009-2024 Ethan Lee and the MonoGame Team
 *
 * Released under the Microsoft Public License.
 * See LICENSE for details.
 */
#endregion

#region Using Statements
using System.Collections.Generic;

using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Microsoft.Xna.Framework.Content
{
	internal class ModelReader : ContentTypeReader<Model>
	{
		#region Public Constructor

		public ModelReader()
		{
		}

		#endregion

		#region Private Bone Helper Method

		private static int ReadBoneReference(ContentReader reader, uint boneCount)
		{
			uint boneId;
			// Read the bone ID, which may be encoded as either an 8 or 32 bit value.
			if (boneCount < 255)
			{
				boneId = reader.ReadByte();
			}
			else
			{
				boneId = reader.ReadUInt32();
			}
			if (boneId != 0)
			{
				return (int) (boneId - 1);
			}

			return -1;
		}

		#endregion

		#region Protected Read Method

		protected internal override Model Read(ContentReader reader, Model existingInstance)
		{
			if (reader.version < 5)
			{
				return ReadXna31(reader, existingInstance);
			}

			// Read the bone names and transforms.
			uint boneCount = reader.ReadUInt32();
			List<ModelBone> bones = new List<ModelBone>((int) boneCount);
			for (uint i = 0; i < boneCount; i += 1)
			{
				string name = reader.ReadObject<string>();
				Matrix matrix = reader.ReadMatrix();
				ModelBone bone = new ModelBone {
					Transform = matrix,
					Index = (int) i,
					Name = name
				};
				bones.Add(bone);
			}
			// Read the bone hierarchy.
			for (int i = 0; i < boneCount; i += 1)
			{
				ModelBone bone = bones[i];
				// Read the parent bone reference.
				int parentIndex = ReadBoneReference(reader, boneCount);
				if (parentIndex != -1)
				{
					bone.Parent = bones[parentIndex];
				}
				// Read the child bone references.
				uint childCount = reader.ReadUInt32();
				if (childCount != 0)
				{
					for (uint j = 0; j < childCount; j += 1)
					{
						int childIndex = ReadBoneReference(reader, boneCount);
						if (childIndex != -1)
						{
							bone.AddChild(bones[childIndex]);
						}
					}
				}
			}

			List<ModelMesh> meshes = new List<ModelMesh>();

			// Read the mesh data.
			int meshCount = reader.ReadInt32();

			GraphicsDevice device = reader.ContentManager.GetGraphicsDevice();

			for (int i = 0; i < meshCount; i += 1)
			{
				string name = reader.ReadObject<string>();
				int parentBoneIndex = ReadBoneReference(reader, boneCount);
				BoundingSphere boundingSphere = reader.ReadBoundingSphere();

				// Tag
				object meshTag = reader.ReadObject<object>();

				// Read the mesh part data.
				int partCount = reader.ReadInt32();

				List<ModelMeshPart> parts = new List<ModelMeshPart>(partCount);

				for (uint j = 0; j < partCount; j += 1)
				{
					ModelMeshPart part;
					if (existingInstance != null)
					{
						part = existingInstance.Meshes[i].MeshParts[(int) j];
					}
					else
					{
						part = new ModelMeshPart();
					}

					part.VertexOffset = reader.ReadInt32();
					part.NumVertices = reader.ReadInt32();
					part.StartIndex = reader.ReadInt32();
					part.PrimitiveCount = reader.ReadInt32();

					// Tag
					part.Tag = reader.ReadObject<object>();

					parts.Add(part);

					int jj = (int) j;
					reader.ReadSharedResource<VertexBuffer>(
						delegate (VertexBuffer v)
						{
							parts[jj].VertexBuffer = v;
						}
					);
					reader.ReadSharedResource<IndexBuffer>(
						delegate (IndexBuffer v)
						{
							parts[jj].IndexBuffer = v;
						}
					);
					reader.ReadSharedResource<Effect>(
						delegate (Effect v)
						{
							parts[jj].Effect = v;
						}
					);
				}
				if (existingInstance != null)
				{
					continue;
				}
				ModelMesh mesh = new ModelMesh(device, parts);
				mesh.Tag = meshTag;
				mesh.Name = name;
				mesh.ParentBone = bones[parentBoneIndex];
				mesh.ParentBone.AddMesh(mesh);
				mesh.BoundingSphere = boundingSphere;
				meshes.Add(mesh);
			}
			if (existingInstance != null)
			{
				// Read past remaining data and return existing instance
				ReadBoneReference(reader, boneCount);
				reader.ReadObject<object>();
				return existingInstance;
			}
			// Read the final pieces of model data.
			int rootBoneIndex = ReadBoneReference(reader, boneCount);
			Model model = new Model(device, bones, meshes);
			model.Root = bones[rootBoneIndex];
			// Tag?
			model.Tag = reader.ReadObject<object>();
			return model;
		}

		#endregion

		#region XNA 3.1 Read Method

		/* XNB v4 lays a Model out differently enough that sharing code with the v5 path would only
		 * hide the differences. Taken from Model::Read / ReadBones / ReadVertexDeclarations /
		 * ReadMeshes / ReadMeshParts in the 3.1 redistributable:
		 *
		 *     int32 boneCount
		 *     per bone:  string name (as an object), Matrix transform
		 *     per bone:  boneRef parent, int32 childCount, boneRef[childCount] children
		 *     int32 declarationCount
		 *     per declaration: object VertexDeclaration
		 *     int32 meshCount
		 *     per mesh:
		 *         string name (as an object)
		 *         boneRef parentBone
		 *         Vector3 boundingCentre, float boundingRadius
		 *         object VertexBuffer          <- raw bytes; see VertexBufferReader
		 *         object IndexBuffer
		 *         object tag
		 *         int32 partCount
		 *         per part:
		 *             int32 streamOffset, baseVertex, numVertices, startIndex, primitiveCount
		 *             int32 vertexDeclarationIndex
		 *             object tag
		 *             sharedResource Effect
		 *     boneRef root
		 *     object tag
		 *
		 * Three things differ from v5 beyond the ordering: the declarations are a model-level array
		 * that parts index (v5 puts the declaration inside each vertex buffer); the vertex and index
		 * buffers are per-MESH inline objects rather than per-PART shared resources; and each part
		 * carries a byte-granular streamOffset that XNA 4 dropped.
		 */
		private static Model ReadXna31(ContentReader reader, Model existingInstance)
		{
			if (existingInstance != null)
			{
				throw new ContentLoadException(
					"Reloading an XNB v4 Model into an existing instance is not supported."
				);
			}

			GraphicsDevice device = reader.ContentManager.GetGraphicsDevice();

			// ---- bones -----------------------------------------------------------------------

			uint boneCount = (uint) reader.ReadInt32();
			List<ModelBone> bones = new List<ModelBone>((int) boneCount);
			for (uint i = 0; i < boneCount; i += 1)
			{
				string name = reader.ReadObject<string>();
				Matrix transform = reader.ReadMatrix();
				bones.Add(new ModelBone
				{
					Transform = transform,
					Index = (int) i,
					Name = name
				});
			}
			for (int i = 0; i < boneCount; i += 1)
			{
				ModelBone bone = bones[i];
				int parentIndex = ReadBoneReference(reader, boneCount);
				if (parentIndex != -1)
				{
					bone.Parent = bones[parentIndex];
				}
				int childCount = reader.ReadInt32();
				for (int j = 0; j < childCount; j += 1)
				{
					int childIndex = ReadBoneReference(reader, boneCount);
					if (childIndex != -1)
					{
						bone.AddChild(bones[childIndex]);
					}
				}
			}

			// ---- the model-level vertex declaration table ------------------------------------

			int declarationCount = reader.ReadInt32();
			VertexDeclaration[] declarations = new VertexDeclaration[declarationCount];
			for (int i = 0; i < declarationCount; i += 1)
			{
				declarations[i] = reader.ReadObject<VertexDeclaration>();
			}

			// ---- meshes ----------------------------------------------------------------------

			int meshCount = reader.ReadInt32();
			List<ModelMesh> meshes = new List<ModelMesh>(meshCount);

			for (int i = 0; i < meshCount; i += 1)
			{
				string name = reader.ReadObject<string>();
				int parentBoneIndex = ReadBoneReference(reader, boneCount);
				BoundingSphere boundingSphere = reader.ReadBoundingSphere();

				// A 3.1 vertex buffer is bytes only; VertexBufferReader parks them here.
				reader.xna31ExpectsRawVertexBuffer = true;
				reader.xna31RawVertexBuffer = null;
				reader.ReadObject<VertexBuffer>();
				byte[] vertexData = reader.xna31RawVertexBuffer;
				reader.xna31ExpectsRawVertexBuffer = false;
				reader.xna31RawVertexBuffer = null;

				IndexBuffer indexBuffer = reader.ReadObject<IndexBuffer>();
				object meshTag = reader.ReadObject<object>();

				int partCount = reader.ReadInt32();
				List<ModelMeshPart> parts = new List<ModelMeshPart>(partCount);
				VertexBuffer vertexBuffer = null;

				for (int j = 0; j < partCount; j += 1)
				{
					int streamOffset = reader.ReadInt32();
					int baseVertex = reader.ReadInt32();
					int numVertices = reader.ReadInt32();
					int startIndex = reader.ReadInt32();
					int primitiveCount = reader.ReadInt32();
					int declarationIndex = reader.ReadInt32();

					VertexDeclaration declaration = declarations[declarationIndex];

					/* Every part of a 3.1 mesh draws out of the one mesh-level vertex buffer, so
					 * the first part's declaration is the buffer's declaration. A mesh whose parts
					 * disagree cannot be expressed as one FNA VertexBuffer, and silently taking the
					 * first would corrupt the rest, so say so instead.
					 */
					if (vertexBuffer == null)
					{
						if (vertexData == null)
						{
							throw new ContentLoadException(
								"XNB v4 mesh '" + name + "' has mesh parts but no vertex buffer."
							);
						}
						vertexBuffer = new VertexBuffer(
							device,
							declaration,
							vertexData.Length / declaration.VertexStride,
							BufferUsage.None
						);
						vertexBuffer.SetData(vertexData);
					}
					else if (vertexBuffer.VertexDeclaration != declaration)
					{
						throw new ContentLoadException(
							"XNB v4 mesh '" + name + "' has mesh parts with different vertex " +
							"declarations over one vertex buffer; FNA binds the declaration to the " +
							"buffer, so this cannot be represented."
						);
					}

					/* streamOffset is a BYTE offset into the vertex buffer that XNA 4 replaced with
					 * a vertex-granular VertexOffset. Whole vertices fold into VertexOffset; a
					 * partial vertex has no FNA equivalent.
					 */
					int vertexOffset = baseVertex;
					if (streamOffset != 0)
					{
						if (streamOffset % declaration.VertexStride != 0)
						{
							throw new ContentLoadException(
								"XNB v4 mesh part has stream offset " + streamOffset +
								", which is not a whole number of " + declaration.VertexStride +
								"-byte vertices."
							);
						}
						vertexOffset += streamOffset / declaration.VertexStride;
					}

					ModelMeshPart part = new ModelMeshPart
					{
						VertexOffset = vertexOffset,
						NumVertices = numVertices,
						StartIndex = startIndex,
						PrimitiveCount = primitiveCount,
						VertexBuffer = vertexBuffer,
						IndexBuffer = indexBuffer
					};
					part.Tag = reader.ReadObject<object>();
					parts.Add(part);

					int jj = j;
					List<ModelMeshPart> capturedParts = parts;
					reader.ReadSharedResource<Effect>(
						delegate (Effect effect)
						{
							capturedParts[jj].Effect = effect;
						}
					);
				}

				ModelMesh mesh = new ModelMesh(device, parts);
				mesh.Tag = meshTag;
				mesh.Name = name;
				mesh.BoundingSphere = boundingSphere;
				if (parentBoneIndex != -1)
				{
					mesh.ParentBone = bones[parentBoneIndex];
					mesh.ParentBone.AddMesh(mesh);
				}
				meshes.Add(mesh);
			}

			// ---- root and tag ----------------------------------------------------------------

			int rootBoneIndex = ReadBoneReference(reader, boneCount);
			Model model = new Model(device, bones, meshes);
			if (rootBoneIndex != -1)
			{
				model.Root = bones[rootBoneIndex];
			}
			model.Tag = reader.ReadObject<object>();
			return model;
		}

		#endregion
	}
}
