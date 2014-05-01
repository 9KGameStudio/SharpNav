﻿using System;
using System.Collections.Generic;

using SharpNav.Geometry;

#if MONOGAME || XNA
using Microsoft.Xna.Framework;
#elif OPENTK
using OpenTK;
#elif SHARPDX
using SharpDX;
#elif UNITY3D
using UnityEngine;
#endif

namespace SharpNav.Collections.Generic
{
	/// <summary>
	/// A tree of bounding volumes.
	/// </summary>
	public class BVTree
	{
		private Node[] nodes;

		/// <summary>
		/// Initializes a new instance of the <see cref="BVTree"/> class.
		/// </summary>
		/// <param name="verts">A set of vertices.</param>
		/// <param name="polys">A set of polygons composed of the vertices in <see cref="verts"/>.</param>
		/// <param name="nvp">The maximum number of vertices per polygon.</param>
		/// <param name="cellSize">The size of a cell.</param>
		/// <param name="cellHeight">The height of a cell.</param>
		public BVTree(Vector3[] verts, PolyMesh.Polygon[] polys, int nvp, float cellSize, float cellHeight)
		{
			nodes = new Node[polys.Length * 2];
			var items = new List<Node>();

			for (int i = 0; i < polys.Length; i++)
			{
				PolyMesh.Polygon p = polys[i];

				Node temp;
				temp.Index = i;
				temp.Bounds.Min = temp.Bounds.Max = verts[p.Vertices[0]];

				for (int j = 1; j < nvp; j++)
				{
					int vi = p.Vertices[j];
					if (vi == PolyMesh.NullId)
						break;

					Vector3 v = verts[vi];
					Vector3Extensions.ComponentMin(ref temp.Bounds.Min, ref v, out temp.Bounds.Min);
					Vector3Extensions.ComponentMax(ref temp.Bounds.Max, ref v, out temp.Bounds.Max);
				}

				temp.Bounds.Min.Y = (int)Math.Floor((float)temp.Bounds.Min.Y * cellHeight / cellSize);
				temp.Bounds.Max.Y = (int)Math.Ceiling((float)temp.Bounds.Max.Y * cellHeight / cellSize);

				items.Add(temp);
			}

			Subdivide(items, 0, items.Count, 0);
		}

		/// <summary>
		/// Gets the number of nodes in the tree.
		/// </summary>
		public int Count
		{
			get
			{
				return nodes.Length;
			}
		}

		/// <summary>
		/// Gets the node at a specified index.
		/// </summary>
		/// <param name="index">The index.</param>
		/// <returns>The node at the index.</returns>
		public Node this[int index]
		{
			get
			{
				return nodes[index];
			}
		}

		/// <summary>
		/// Subdivides a list of bounding boxes until it is a tree.
		/// </summary>
		/// <param name="items">A list of bounding boxes.</param>
		/// <param name="minIndex">The first index to consider (recursively).</param>
		/// <param name="maxIndex">The last index to consier (recursively).</param>
		/// <param name="curNode">The current node to look at.</param>
		/// <returns>The current node at the end of each method.</returns>
		private int Subdivide(List<Node> items, int minIndex, int maxIndex, int curNode)
		{
			int numIndex = maxIndex - minIndex;
			int curIndex = curNode;

			int oldNode = curNode;
			curNode++;

			//Check if the current node is a leaf node
			if (numIndex == 1)
				nodes[oldNode] = items[minIndex];
			else
			{
				BBox3 bounds;
				CalcExtends(items, minIndex, maxIndex, out bounds);
				nodes[oldNode].Bounds = bounds;

				int axis = LongestAxis((int)(bounds.Max.X - bounds.Min.X), (int)(bounds.Max.Y - bounds.Min.Y), (int)(bounds.Max.Z - bounds.Min.Z));

				switch (axis)
				{
					case 0:
						items.Sort(minIndex, numIndex, new CompareX());
						break;
					case 1:
						items.Sort(minIndex, numIndex, new CompareY());
						break;
					case 2:
						items.Sort(minIndex, numIndex, new CompareZ());
						break;
					default:
						break;
				}

				int splitIndex = minIndex + (numIndex / 2);

				curNode = Subdivide(items, minIndex, splitIndex, curNode);
				curNode = Subdivide(items, splitIndex, maxIndex, curNode);

				int escapeIndex = curNode - curIndex;
				nodes[oldNode].Index = -escapeIndex;
			}

			return curNode;
		}

		/// <summary>
		/// Calculates the bounding box for a set of bounding boxes.
		/// </summary>
		/// <param name="items">The list of all the bounding boxes.</param>
		/// <param name="minIndex">The first bounding box in the list to get the extends of.</param>
		/// <param name="maxIndex">The last bounding box in the list to get the extends of.</param>
		/// <param name="bounds">The extends of all the bounding boxes.</param>
		private void CalcExtends(List<Node> items, int minIndex, int maxIndex, out BBox3 bounds)
		{
			bounds = items[minIndex].Bounds;

			for (int i = minIndex + 1; i < maxIndex; i++)
			{
				Node it = items[i];
				Vector3Extensions.ComponentMin(ref it.Bounds.Min, ref bounds.Min, out bounds.Min);
				Vector3Extensions.ComponentMax(ref it.Bounds.Max, ref bounds.Max, out bounds.Max);
			}
		}

		/// <summary>
		/// Determine whether the bounding x, y, or z axis contains the longest distance 
		/// </summary>
		/// <param name="x">Length of bounding x-axis</param>
		/// <param name="y">Length of bounding y-axis</param>
		/// <param name="z">Length of bounding z-axis</param>
		/// <returns>Returns the a specific axis (x, y, or z)</returns>
		private int LongestAxis(int x, int y, int z)
		{
			int axis = 0;
			int max = x;

			if (y > max)
			{
				axis = 1;
				max = y;
			}

			if (z > max)
				axis = 2;

			return axis;
		}

		/// <summary>
		/// The data stored in a bounding volume node.
		/// </summary>
		public struct Node
		{
			public BBox3 Bounds;
			public int Index;
		}

		/// <summary>
		/// An <see cref="IComparer{T}"/> implementation that only compares two <see cref="Node"/>s on the X axis.
		/// </summary>
		public class CompareX : IComparer<Node>
		{
			/// <summary>
			/// Compares two nodes's bounds on the X axis.
			/// </summary>
			/// <param name="a">A node.</param>
			/// <param name="b">Another node.</param>
			/// <returns>A negative value if a is less than b; 0 if they are equal; a positive value of a is greater than b.</returns>
			public int Compare(Node a, Node b)
			{
				if (a.Bounds.Min.X < b.Bounds.Min.X)
					return -1;

				if (a.Bounds.Min.X > b.Bounds.Min.X)
					return 1;

				return 0;
			}
		}

		/// <summary>
		/// An <see cref="IComparer{T}"/> implementation that only compares two <see cref="Node"/>s on the Y axis.
		/// </summary>
		public class CompareY : IComparer<Node>
		{
			/// <summary>
			/// Compares two nodes's bounds on the Y axis.
			/// </summary>
			/// <param name="a">A node.</param>
			/// <param name="b">Another node.</param>
			/// <returns>A negative value if a is less than b; 0 if they are equal; a positive value of a is greater than b.</returns>
			public int Compare(Node a, Node b)
			{
				if (a.Bounds.Min.Y < b.Bounds.Min.Y)
					return -1;

				if (a.Bounds.Min.Y > b.Bounds.Min.Y)
					return 1;

				return 0;
			}
		}

		/// <summary>
		/// An <see cref="IComparer{T}"/> implementation that only compares two <see cref="Node"/>s on the Z axis.
		/// </summary>
		public class CompareZ : IComparer<Node>
		{
			/// <summary>
			/// Compares two nodes's bounds on the Z axis.
			/// </summary>
			/// <param name="a">A node.</param>
			/// <param name="b">Another node.</param>
			/// <returns>A negative value if a is less than b; 0 if they are equal; a positive value of a is greater than b.</returns>
			public int Compare(Node a, Node b)
			{
				if (a.Bounds.Min.Z < b.Bounds.Min.Z)
					return -1;

				if (a.Bounds.Min.Z > b.Bounds.Min.Z)
					return 1;

				return 0;
			}
		}
	}
}
