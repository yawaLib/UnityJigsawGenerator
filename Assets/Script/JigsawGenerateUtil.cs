﻿using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.VectorGraphics;

namespace Jigsaw
{
	public class JigsawParam
	{
		public Transform Parent;
		public int Horizontal;
		public int Vertical;
		public VectorUtils.TessellationOptions Option;
		public Material Material;
		public float Thickness;
		public Texture Texture;
		// 塗りの厚み
		public float DrawThickness = 2f;
		public Color LineColor = Color.gray;

		public Vector2 Size => new Vector2(Horizontal, Vertical);
		public Vector2 TextureSize => Texture == null ? Vector2.one : new Vector2(Texture.width, Texture.height);
		
	}
	
	internal class Border
	{
		public readonly Vector2Int Begin;
		public readonly Vector2Int End;
		public readonly List<BezierPathSegment> Segments;

		public Border(Vector2Int begin, Vector2Int end, List<BezierPathSegment> segments)
		{
			Begin = begin;
			End = end;
			Segments = segments;
		}
	}

	public static class JigsawGenerateUtil
	{
		// 反時計回り
		private static readonly Vector2Int[] CWPosition = {Vector2Int.zero, Vector2Int.right, Vector2Int.one, Vector2Int.up};

		// 時計回り
		private static readonly Vector2Int[] CCWPosition = {Vector2Int.zero, Vector2Int.up, Vector2Int.one, Vector2Int.right};

		public static void GenerateJigsaw(JigsawParam param)
		{
			var shapes = new List<Shape>(param.Horizontal * param.Vertical);
			var borders = new List<Border>(param.Horizontal * param.Vertical * 4);
			GeneratePieceShapes(param, ref shapes, ref borders);
			GenerateMesh(param, shapes);
			var texture = TextureUtil.CreateTexture(param, borders);
			if (param.Material != null)
			{
				param.Material.mainTexture = texture;
			}
		}

		/// <summary>
		/// ピース生成のためのShapeを作成
		/// </summary>
		/// <returns></returns>
		private static void GeneratePieceShapes(JigsawParam param, ref List<Shape> shapes, ref List<Border> borders)
		{
			// 境界線を作成
			for (var x = 0; x < param.Horizontal; x++)
			{
				for (var y = 0; y < param.Vertical; y++)
				{
					shapes.Add(new Shape
					{
						Fill = new SolidFill(),
						Contours = new[]
						{
							new BezierContour
							{
								Segments = GetPieceSegment(param, x, y, ref borders).ToArray(),
								Closed = true,
							}
						},
					});
				}
			}
		}

		/// <summary>
		/// ピースの外線を取得
		/// </summary>
		private static List<BezierPathSegment> GetPieceSegment(JigsawParam param, int x, int y, ref List<Border> borders)
		{
			var ret = new List<BezierPathSegment>();
			var isClockwise = (x + y) % 2 == 0;
			foreach (var position in isClockwise ? CWPosition : CCWPosition)
			{
				var begin = new Vector2Int(x + position.x, y + position.y);
				var end = isClockwise
					? new Vector2Int(position.y == 0 ? x + 1 : x, y + position.x)
					: new Vector2Int(x + position.y, position.x == 0 ? y + 1 : y);

				// 端のピース
				if (
					begin.x == 0 && end.x == 0 ||
					begin.y == 0 && end.y == 0 ||
					begin.x == param.Horizontal && end.x == param.Horizontal ||
					begin.y == param.Vertical && end.y == param.Vertical
				)
				{
					ret.Add(new BezierPathSegment
					{
						P0 = begin / param.Size,
						P1 = end / param.Size,
						P2 = begin / param.Size,
					});

					continue;
				}

				var border = borders.Find(b => b.Begin == begin && b.End == end);
				if (border == null)
				{
					// 分割数に応じて凸凹の高さを決める
					var height = Math.Abs(begin.x - end.x) < 0.001f
						? 1 / (float) param.Horizontal * param.Horizontal / 4f
						: 1 / (float) param.Vertical * param.Vertical / 4f;
					
					border = new Border(begin, end, GetBorder(begin / param.Size, end / param.Size, height));
					borders.Add(border);
				}

				ret.AddRange(border.Segments);
			}

			return ret;
		}

		/// <summary>
		/// Shapeを元にMeshを作成
		/// </summary>
		private static void GenerateMesh(JigsawParam param, List<Shape> shapes)
		{
			var pieces = new Transform[shapes.Count];
			for (var i = 0; i < shapes.Count; ++i)
			{
				var scene = new Scene {Root = new SceneNode {Shapes = new List<Shape> {shapes[i]}}};
				
				var position = new Vector2Int(
					Mathf.FloorToInt(i / (float)param.Vertical),
					Mathf.FloorToInt(i % (float)param.Vertical)
				);

				var mesh = new Mesh();
				var obj = new GameObject(position.ToString());
				var geometries = VectorUtils.TessellateScene(scene, param.Option);

				var meshRenderer = obj.AddComponent<MeshRenderer>();
				meshRenderer.material = param.Material;
				var meshFilter = obj.AddComponent<MeshFilter>();
				VectorUtils.FillMesh(mesh, geometries, 1f);
				
				// uv set
				mesh.uv = mesh.vertices.Select(v => new Vector2(v.x, v.y)).ToArray(); 
				
				if (param.Texture != null)
				{
					mesh.vertices = mesh.vertices
						.Select(v => new Vector3(v.x * param.Texture.width, v.y * param.Texture.height, v.z))
						.ToArray();
				}
				
				// Pivot の更新
				// 1ピースのサイズ
				var size = param.TextureSize / param.Size;

				mesh.vertices = mesh.vertices
					.Select(v => new Vector3(
						v.x - size.x * position.x - size.x / 2f,
						v.y - size.y * position.y - size.y / 2f,
						v.z)
					).ToArray();
				
				MeshUtil.PushMesh(mesh, param.Thickness);
				
				mesh.RecalculateBounds();
				mesh.RecalculateNormals();
				mesh.RecalculateTangents();
				meshFilter.mesh = mesh;

				obj.transform.SetParent(param.Parent);
				obj.transform.localPosition = -size * param.Size / 2f + size * position + size / 2f;
				pieces[i] = obj.transform;
			}
		}
		
		/// <summary>
		/// 境界線を取得
		/// </summary>
		private static List<BezierPathSegment> GetBorder(Vector2 begin, Vector2 end, float heightRatio)
		{
			var segments = new List<BezierPathSegment>();
			var isVertical = Math.Abs(begin.x - end.x) < 0.001f;
			var reverse = UnityEngine.Random.Range(0, 2) == 0 ? 1 : -1;
			var height = (begin - end).magnitude * heightRatio;
			var positions = new[]
			{
				0f, 0.1f, 0.3f,
				0.4f, 0.4f, 0.3f,
				0.3f, 0.3f, 0.7f,
				0.7f, 0.7f, 0.6f,
				0.6f, 0.7f, 1f,
			};
			var heights = new[]
			{
				0f, 0f, 0f,
				0f, height / 3f, height / 3f,
				height / 2f, height, height,
				height / 2f, height / 3f, height / 3f,
				0f, 0f, 0f,
			};

			var heightBase = (isVertical ? Vector2.right : Vector2.up) * reverse;
			for (var i = 0; i < positions.Length; i += 3)
			{
				segments.Add(new BezierPathSegment
				{
					P0 = Vector2.Lerp(begin, end, positions[i]) + heightBase * heights[i],
					P1 = Vector2.Lerp(begin, end, positions[i + 1]) + heightBase * heights[i + 1],
					P2 = Vector2.Lerp(begin, end, positions[i + 2]) + heightBase * heights[i + 2],
				});
			}

			return segments;
		}
	}
}