﻿#region License
/**
 * Copyright (c) 2013-2014 Robert Rouhani <robert.rouhani@gmail.com> and other contributors (see CONTRIBUTORS file).
 * Licensed under the MIT License - https://raw.github.com/Robmaister/SharpNav/master/LICENSE
 */
#endregion

using System;
using System.Collections.Generic;

using SharpNav.Geometry;

namespace SharpNav
{
	//TODO should this be ISet<Contour>? Are the extra methods useful?
	public class ContourSet : ICollection<Contour>
	{
		private List<Contour> contours;
		private BBox3 bounds;
		private float cellSize;
		private float cellHeight;
		private int width;
		private int height;
		private int borderSize;

		/// <summary>
		/// Initializes a new instance of the <see cref="ContourSet"/> class by tracing edges around the regions generated by the
		/// <see cref="CompactHeightfield"/>.
		/// </summary>
		/// <param name="compactField">The <see cref="CompactHeightfield"/> containing regions.</param>
		/// <param name="maxError">The maximum amount of error allowed in simplification.</param>
		/// <param name="maxEdgeLen">The maximum length of an edge.</param>
		/// <param name="buildFlags">The settings for how contours should be built.</param>
		public ContourSet(CompactHeightfield compactField, float maxError, int maxEdgeLen, ContourBuildFlags buildFlags)
		{
			//copy the CompactHeightfield data into ContourSet
			this.bounds = compactField.Bounds;

			if (compactField.BorderSize > 0)
			{
				//remove offset
				float pad = compactField.BorderSize * compactField.CellSize;
				this.bounds.Min.X += pad;
				this.bounds.Min.Z += pad;
				this.bounds.Max.X -= pad;
				this.bounds.Max.Z -= pad;
			}

			this.cellSize = compactField.CellSize;
			this.cellHeight = compactField.CellHeight;
			this.width = compactField.Width - compactField.BorderSize * 2;
			this.height = compactField.Height - compactField.BorderSize * 2;
			this.borderSize = compactField.BorderSize;

			int maxContours = Math.Max((int)compactField.MaxRegions, 8);
			contours = new List<Contour>(maxContours);

			int[] flags = new int[compactField.Spans.Length];

			//Modify flags array by using the CompactHeightfield data
			//mark boundaries
			for (int y = 0; y < compactField.Length; y++)
			{
				for (int x = 0; x < compactField.Width; x++)
				{
					//loop through all the spans in the cell
					CompactCell c = compactField.Cells[x + y * compactField.Width];
					for (int i = c.StartIndex, end = c.StartIndex + c.Count; i < end; i++)
					{
						int res = 0;
						CompactSpan s = compactField.Spans[i];

						//set the flag to 0 if the region is a border region or null.
						if (Region.IsBorderOrNull(compactField.Spans[i].Region))
						{
							flags[i] = 0;
							continue;
						}

						//go through all the neighboring cells
						for (var dir = Direction.West; dir <= Direction.South; dir++)
						{
							//obtain region id
							RegionId r = 0;
							if (s.IsConnected(dir))
							{
								int dx = x + dir.GetHorizontalOffset();
								int dy = y + dir.GetVerticalOffset();
								int di = compactField.Cells[dx + dy * compactField.Width].StartIndex + CompactSpan.GetConnection(ref s, dir);
								r = compactField.Spans[di].Region;
							}

							//region ids are equal
							if (r == compactField.Spans[i].Region)
							{
								//res marks all the INTERNAL edges
								MarkInternalEdges(ref res, dir);
							}
						}

						//flags represents all the nonconnected edges, edges that are only internal
						//the edges need to be between different regions
						flags[i] = FlipAllBits(res); 
					}
				}
			}

			var verts = new List<ContourVertex>();
			var simplified = new List<ContourVertex>();

			for (int y = 0; y < compactField.Length; y++)
			{
				for (int x = 0; x < compactField.Width; x++)
				{
					CompactCell c = compactField.Cells[x + y * compactField.Width];
					for (int i = c.StartIndex, end = c.StartIndex + c.Count; i < end; i++)
					{
						//flags is either 0000 or 1111
						//in other words, not connected at all 
						//or has all connections, which means span is in the middle and thus not an edge.
						if (flags[i] == 0 || flags[i] == 0xf)
						{
							flags[i] = 0;
							continue;
						}

						RegionId reg = compactField.Spans[i].Region;
						if (Region.IsBorderOrNull(reg))
							continue;
						
						//reset each iteration
						verts.Clear();
						simplified.Clear();

						//Mark points, which are basis of contous, intially with "verts"
						//Then, simplify "verts" to get "simplified"
						//Finally, clean up the "simplified" data
						WalkContour(x, y, i, compactField, flags, verts);
						SimplifyContour(verts, simplified, maxError, maxEdgeLen, buildFlags);
						RemoveDegenerateSegments(simplified);

						if (simplified.Count >= 3)
							contours.Add(new Contour(simplified, verts, reg, compactField.Areas[i], borderSize));
					}
				}
			}

			//Check and merge bad contours
			for (int i = 0; i < contours.Count; i++)
			{
				Contour cont = contours[i];

				//Check if contour is backwards
				if (cont.Area2D < 0)
				{
					//Find another contour to merge with
					int mergeIndex = -1;
					for (int j = 0; j < contours.Count; j++)
					{
						if (i == j)
							continue;

						//Must have at least one vertex, the same region ID, and be going forwards.
						Contour contj = contours[j];
						if (contj.Vertices.Length != 0 && contj.RegionId == cont.RegionId && contj.Area2D > 0)
						{
							mergeIndex = j;
							break;
						}
					}

					//Merge if found.
					if (mergeIndex != -1)
						contours[mergeIndex].MergeWith(cont);
				}
			}
		}

		public int Count
		{
			get
			{
				return contours.Count;
			}
		}

		public BBox3 Bounds
		{
			get
			{
				return bounds;
			}
		}

		public float CellSize
		{
			get
			{
				return cellSize;
			}
		}

		public float CellHeight
		{
			get
			{
				return cellHeight;
			}
		}

		public int Width
		{
			get
			{
				return width;
			}
		}

		public int Height
		{
			get
			{
				return height;
			}
		}

		public int BorderSize
		{
			get
			{
				return borderSize;
			}
		}

		bool ICollection<Contour>.IsReadOnly
		{
			get { return true; }
		}

		public bool Contains(Contour item)
		{
			return contours.Contains(item);
		}

		public void CopyTo(Contour[] array, int arrayIndex)
		{
			contours.CopyTo(array, arrayIndex);
		}

		public IEnumerator<Contour> GetEnumerator()
		{
			return contours.GetEnumerator();
		}

		//TODO support the extra ICollection methods later?
		void ICollection<Contour>.Add(Contour item)
		{
			throw new InvalidOperationException();
		}

		void ICollection<Contour>.Clear()
		{
			throw new InvalidOperationException();
		}

		bool ICollection<Contour>.Remove(Contour item)
		{
			throw new InvalidOperationException();
		}

		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}

		private static void MarkInternalEdges(ref int flag, Direction dir)
		{
			//flag represented as 4 bits (left bit represents dir = 3, right bit represents dir = 0)
			//default is 0000
			//the |= operation sets each direction bit to 1 (so if dir = 0, 0000 -> 0001)
			flag |= 1 << (int)dir;
		}

		private static int FlipAllBits(int flag)
		{
			//flips all the bits in res
			//0000 (completely internal) -> 1111
			//1111 (no internal edges) -> 0000
			return flag ^ 0xf;
		}

		private static bool IsConnected(int flag, Direction dir)
		{
			//four bits, each bit represents a direction (0 = non-connected, 1 = connected)
			return (flag & (1 << (int)dir)) != 0;
		}

		private static void RemoveVisited(ref int flag, Direction dir)
		{
			//say flag = 0110
			//dir = 2 (so 1 << dir = 0100)
			//~dir = 1011
			//flag &= ~dir
			//flag = 0110 & 1011 = 0010
			flag &= ~(1 << (int)dir); // remove visited edges
		}

		/// <summary>
		/// Initial generation of the contours
		/// </summary>
		/// <param name="x">Cell x</param>
		/// <param name="y">Cell y</param>
		/// <param name="i">Span index</param>
		/// <param name="compactField">CompactHeightfield</param>
		/// <param name="flags">?</param>
		/// <param name="points">The vertices of a contour.</param>
		private void WalkContour(int x, int y, int i, CompactHeightfield compactField, int[] flags, List<ContourVertex> points)
		{
			Direction dir = 0;

			//find the first direction that has a connection 
			while (!IsConnected(flags[i], dir))
				dir++;

			Direction startDir = dir;
			int starti = i;

			AreaId area = compactField.Areas[i];

			int iter = 0;
			while (++iter < 40000)
			{
				// this direction is connected
				if (IsConnected(flags[i], dir))
				{
					// choose the edge corner
					bool isBorderVertex;
					bool isAreaBorder = false;

					int px = x;
					int py = GetCornerHeight(x, y, i, dir, compactField, out isBorderVertex);
					int pz = y;

					switch (dir)
					{
						case Direction.West:
							pz++;
							break;
						case Direction.North:
							px++;
							pz++;
							break;
						case Direction.East:
							px++;
							break;
					}

					RegionId r = 0;
					CompactSpan s = compactField.Spans[i];
					if (s.IsConnected(dir))
					{
						int dx = x + dir.GetHorizontalOffset();
						int dy = y + dir.GetVerticalOffset();
						int di = compactField.Cells[dx + dy * compactField.Width].StartIndex + CompactSpan.GetConnection(ref s, dir);
						r = compactField.Spans[di].Region;
						if (area != compactField.Areas[di])
							isAreaBorder = true;
					}
					
					// apply flags if neccessary
					if (isBorderVertex)
						Region.SetBorderVertex(ref r);

					if (isAreaBorder)
						Region.SetAreaBorder(ref r);
					
					//save the point
					points.Add(new ContourVertex(px, py, pz, r));

					RemoveVisited(ref flags[i], dir);	// remove visited edges
					dir = dir.NextClockwise();			// rotate clockwise
				}
				else
				{
					//get a new cell(x, y) and span index(i)
					int di = -1;
					int dx = x + dir.GetHorizontalOffset();
					int dy = y + dir.GetVerticalOffset();
					
					CompactSpan s = compactField.Spans[i];
					if (s.IsConnected(dir))
					{
						CompactCell dc = compactField.Cells[dx + dy * compactField.Width];
						di = dc.StartIndex + CompactSpan.GetConnection(ref s, dir);
					}
					
					if (di == -1)
					{
						// shouldn't happen
						// TODO if this shouldn't happen, this check shouldn't be necessary.
						throw new InvalidOperationException("Something went wrong");
					}
					
					x = dx;
					y = dy;
					i = di;
					dir = dir.NextCounterClockwise(); // rotate counterclockwise
				}

				if (starti == i && startDir == dir)
				{
					break;
				}
			}
		}

		/// <summary>
		/// Helper method for WalkContour function
		/// </summary>
		/// <param name="x">Cell x</param>
		/// <param name="y">Cell y</param>
		/// <param name="i">Span index i</param>
		/// <param name="dir">Direction (west, north, east, south)</param>
		/// <param name="openField">OpenHeightfield</param>
		/// <param name="isBorderVertex">Determine whether the vertex is a border or not</param>
		/// <returns></returns>
		private int GetCornerHeight(int x, int y, int i, Direction dir, CompactHeightfield openField, out bool isBorderVertex)
		{
			isBorderVertex = false;

			CompactSpan s = openField.Spans[i];
			int cornerHeight = s.Minimum;
			Direction dirp = dir.NextClockwise(); //new clockwise direction

			uint[] regs = { 0, 0, 0, 0 };

			//combine region and area codes in order to prevent border vertices, which are in between two areas, to be removed 
			regs[0] = (uint)((int)openField.Spans[i].Region | ((byte)openField.Areas[i] << 16));

			if (s.IsConnected(dir))
			{
				//get neighbor span
				int dx = x + dir.GetHorizontalOffset();
				int dy = y + dir.GetVerticalOffset();
				int di = openField.Cells[dx + dy * openField.Width].StartIndex + CompactSpan.GetConnection(ref s, dir);
				CompactSpan ds = openField.Spans[di];

				cornerHeight = Math.Max(cornerHeight, ds.Minimum);
				regs[1] = (uint)((int)openField.Spans[di].Region | ((byte)openField.Areas[di] << 16));

				//get neighbor of neighbor's span
				if (ds.IsConnected(dirp))
				{
					int dx2 = dx + dirp.GetHorizontalOffset();
					int dy2 = dy + dirp.GetVerticalOffset();
					int di2 = openField.Cells[dx2 + dy2 * openField.Width].StartIndex + CompactSpan.GetConnection(ref ds, dirp);
					CompactSpan ds2 = openField.Spans[di2];

					cornerHeight = Math.Max(cornerHeight, ds2.Minimum);
					regs[2] = (uint)((int)openField.Spans[di2].Region | ((byte)openField.Areas[di2] << 16));
				}
			}

			//get neighbor span
			if (s.IsConnected(dirp))
			{
				int dx = x + dirp.GetHorizontalOffset();
				int dy = y + dirp.GetVerticalOffset();
				int di = openField.Cells[dx + dy * openField.Width].StartIndex + CompactSpan.GetConnection(ref s, dirp);
				CompactSpan ds = openField.Spans[di];

				cornerHeight = Math.Max(cornerHeight, ds.Minimum);
				regs[3] = (uint)((int)openField.Spans[di].Region | ((byte)openField.Areas[di] << 16));

				//get neighbor of neighbor's span
				if (ds.IsConnected(dir))
				{
					int dx2 = dx + dir.GetHorizontalOffset();
					int dy2 = dy + dir.GetVerticalOffset();
					int di2 = openField.Cells[dx2 + dy2 * openField.Width].StartIndex + CompactSpan.GetConnection(ref ds, dir);
					CompactSpan ds2 = openField.Spans[di2];

					cornerHeight = Math.Max(cornerHeight, ds2.Minimum);
					regs[2] = (uint)((int)openField.Spans[di2].Region | ((byte)openField.Areas[di2] << 16));
				}
			}

			//check if vertex is special edge vertex
			//if so, these vertices will be removed later
			for (int j = 0; j < 4; j++)
			{
				int a = j;
				int b = (j + 1) % 4;
				int c = (j + 2) % 4;
				int d = (j + 3) % 4;

				//the vertex is a border vertex if:
				//two same exterior cells in a row followed by two interior cells and none of the regions are out of bounds
				bool twoSameExteriors = Region.IsBorder((RegionId)regs[a]) && Region.IsBorder((RegionId)regs[b]) && regs[a] == regs[b];
				bool twoSameInteriors = !(Region.IsBorder((RegionId)regs[c]) || Region.IsBorder((RegionId)regs[d]));
				bool intsSameArea = (regs[c] >> 16) == (regs[d] >> 16);
				bool noZeros = regs[a] != 0 && regs[b] != 0 && regs[c] != 0 && regs[d] != 0;
				if (twoSameExteriors && twoSameInteriors && intsSameArea && noZeros)
				{
					isBorderVertex = true;
					break;
				}
			}

			return cornerHeight;
		}

		/// <summary>
		/// Simplify the contours by reducing the number of edges
		/// </summary>
		/// <param name="points">Initial vertices</param>
		/// <param name="simplified">New and simplified vertices</param>
		private void SimplifyContour(List<ContourVertex> points, List<ContourVertex> simplified, float maxError, int maxEdgeLen, ContourBuildFlags buildFlags)
		{
			//add initial points
			bool hasConnections = false;
			for (int i = 0; i < points.Count; i++)
			{
				if (Region.RemoveFlags(points[i].RegionId) != 0)
				{
					hasConnections = true;
					break;
				}
			}

			if (hasConnections)
			{
				//contour has some portals to other regions
				//add new point to every location where region changes
				for (int i = 0, end = points.Count; i < end; i++)
				{
					int ii = (i + 1) % end;
					bool differentRegions = !Region.IsSameRegion(points[i].RegionId, points[ii].RegionId);
					bool areaBorders = !Region.IsSameArea(points[i].RegionId, points[ii].RegionId);
					
					if (differentRegions || areaBorders)
					{
						simplified.Add(new ContourVertex(points[i], i));
					}
				}
			}

			//add some points if thhere are no connections
			if (simplified.Count == 0)
			{
				//find lower-left and upper-right vertices of contour
				int lowerLeftX = points[0].X;
				int lowerLeftY = points[0].Y;
				int lowerLeftZ = points[0].Z;
				RegionId lowerLeftI = 0;
				
				int upperRightX = points[0].X;
				int upperRightY = points[0].Y;
				int upperRightZ = points[0].Z;
				RegionId upperRightI = 0;
				
				//iterate through points
				for (int i = 0; i < points.Count; i++)
				{
					int x = points[i].X;
					int y = points[i].Y;
					int z = points[i].Z;
					
					if (x < lowerLeftX || (x == lowerLeftX && z < lowerLeftZ))
					{
						lowerLeftX = x;
						lowerLeftY = y;
						lowerLeftZ = z;
						lowerLeftI = (RegionId)i;
					}
					
					if (x > upperRightX || (x == upperRightX && z > upperRightZ))
					{
						upperRightX = x;
						upperRightY = y;
						upperRightZ = z;
						upperRightI = (RegionId)i;
					}
				}
				
				//save the points
				simplified.Add(new ContourVertex(lowerLeftX, lowerLeftY, lowerLeftZ, lowerLeftI));
				simplified.Add(new ContourVertex(upperRightX, upperRightY, upperRightZ, upperRightI));
			}

			//add points until all points are within erorr tolerance of simplified slope
			int numPoints = points.Count;
			for (int i = 0; i < simplified.Count;)
			{
				int ii = (i + 1) % simplified.Count;

				//obtain (x, z) coordinates, along with region id
				int ax = simplified[i].X;
				int az = simplified[i].Z;
				RegionId ai = simplified[i].RegionId;

				int bx = simplified[ii].X;
				int bz = simplified[ii].Z;
				RegionId bi = simplified[ii].RegionId;

				float maxDeviation = 0;
				int maxi = -1;
				int ci, cIncrement, endi;

				//traverse segment in lexilogical order (try to go from smallest to largest coordinates?)
				if (bx > ax || (bx == ax && bz > az))
				{
					cIncrement = 1;
					ci = (int)(ai + cIncrement) % numPoints;
					endi = (int)bi;
				}
				else
				{
					cIncrement = numPoints - 1;
					ci = (int)(bi + cIncrement) % numPoints;
					endi = (int)ai;
				}

				//tessellate only outer edges or edges between areas
				if (Region.RemoveFlags(points[ci].RegionId) == 0 || Region.IsAreaBorder(points[ci].RegionId))
				{
					//find the maximum deviation
					while (ci != endi)
					{
						float deviation = MathHelper.Distance.PointToSegment2DSquared(points[ci].X, points[ci].Z, ax, az, bx, bz);
						
						if (deviation > maxDeviation)
						{
							maxDeviation = deviation;
							maxi = ci;
						}

						ci = (ci + cIncrement) % numPoints;
					}
				}

				//If max deviation is larger than accepted error, add new point
				if (maxi != -1 && maxDeviation > (maxError * maxError))
				{
					//add extra space to list
					simplified.Add(new ContourVertex(0, 0, 0, 0));

					//make space for new point by shifting elements to the right
					//ex: element at index 5 is now at index 6, since array[6] takes the value of array[6 - 1]
					for (int j = simplified.Count - 1; j > i; j--)
					{
						simplified[j] = simplified[j - 1];
					}

					//add point 
					simplified[i + 1] = new ContourVertex(points[maxi], maxi);
				}
				else
				{
					i++;
				}
			}

			//split too long edges
			if (maxEdgeLen > 0 && (buildFlags & (ContourBuildFlags.TessellateAreaEdges | ContourBuildFlags.TessellateWallEdges)) != 0)
			{
				for (int i = 0; i < simplified.Count;)
				{
					int ii = (i + 1) % simplified.Count;

					//get (x, z) coordinates along with region id
					int ax = simplified[i].X;
					int az = simplified[i].Z;
					RegionId ai = simplified[i].RegionId;

					int bx = simplified[ii].X;
					int bz = simplified[ii].Z;
					RegionId bi = simplified[ii].RegionId;

					//find maximum deviation from segment
					int maxi = -1;
					int ci = (int)(ai + 1) % numPoints;

					//tessellate only outer edges or edges between areas
					bool tess = false;

					//wall edges
					if ((buildFlags & ContourBuildFlags.TessellateWallEdges) != 0 && Region.RemoveFlags(points[ci].RegionId) == 0)
						tess = true;

					//edges between areas
					if ((buildFlags & ContourBuildFlags.TessellateAreaEdges) != 0 && Region.IsAreaBorder(points[ci].RegionId))
						tess = true;

					if (tess)
					{
						int dx = bx - ax;
						int dz = bz - az;
						if (dx * dx + dz * dz > maxEdgeLen * maxEdgeLen)
						{
							//round based on lexilogical direction (smallest to largest cooridinates, first by x.
							//if x coordinates are equal, then compare z coordinates)
							int n = bi < ai ? (bi + numPoints - ai) : (bi - ai);
							
							if (n > 1)
							{
								if (bx > ax || (bx == ax && bz > az))
									maxi = (int)(ai + n / 2) % numPoints;
								else
									maxi = (int)(ai + (n + 1) / 2) % numPoints;
							}
						}
					}

					//add new point
					if (maxi != -1)
					{
						//add extra space to list
						simplified.Add(new ContourVertex(0, 0, 0, 0));

						//make space for new point by shifting elements to the right
						//ex: element at index 5 is now at index 6, since array[6] takes the value of array[6 - 1]
						for (int j = simplified.Count - 1; j > i; j--)
						{
							simplified[j] = simplified[j - 1];
						}

						//add point
						simplified[i + 1] = new ContourVertex(points[maxi], maxi);
					}
					else
					{
						i++;
					}
				}
			}

			for (int i = 0; i < simplified.Count; i++)
			{
				ContourVertex sv = simplified[i];

				//take edge vertex flag from current raw point and neighbor region from next raw point
				int ai = (int)(sv.RegionId + 1) % numPoints;
				RegionId bi = sv.RegionId;

				//save new region id
				sv.RegionId = (points[ai].RegionId & ((RegionId)Region.IdMask | RegionId.AreaBorder)) | (points[(int)bi].RegionId & RegionId.VertexBorder);

				simplified[i] = sv;
			}
		}

		/// <summary>
		/// Clean up the simplified segments
		/// </summary>
		/// <param name="simplified"></param>
		private void RemoveDegenerateSegments(List<ContourVertex> simplified)
		{
			//remove adjacent vertices which are equal on the xz-plane
			for (int i = 0; i < simplified.Count; i++)
			{
				int ni = i + 1;
				if (ni >= simplified.Count)
					ni = 0;

				if (simplified[i].X == simplified[ni].X &&
					simplified[i].Z == simplified[ni].Z)
				{
					//remove degenerate segment
					simplified.RemoveAt(i);
				}
			}
		}
	}
}
