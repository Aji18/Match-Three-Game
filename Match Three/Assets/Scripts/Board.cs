using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DG.Tweening;
using UnityEngine;
using Random = UnityEngine.Random;

namespace MatchThree
{
	public sealed class Board : MonoBehaviour
	{
		[SerializeField] private TileTypeAsset[] tileTypes;

		// [SerializeField] private Row[] rows;

		private Node[,] _nodes;
		public const int RowCount = 8;
		public const int ColumnCount = 8;
		[SerializeField] private Node nodePrefab;
		
		[SerializeField] private AudioClip matchSound;

		[SerializeField] private AudioSource audioSource;

		[SerializeField] private float tweenDuration;

		[SerializeField] private Transform swappingOverlay;

		[SerializeField] private bool ensureNoStartingMatches;

		private readonly List<Node> _selection = new List<Node>();

		private const int nodeSize = 100;

		private bool _isSwapping;
		private bool _isMatching;
		private bool _isShuffling;

		public event Action<TileTypeAsset, int> OnMatch;

		private TileData[,] Matrix
		{
			get
			{
				

				var data = new TileData[RowCount, ColumnCount];

				for (var y = 0; y < ColumnCount; y++)
					for (var x = 0; x < RowCount; x++)
					{
						data[x, y] = GetTile(x, y).Data;
					}
				return data;
			}
		}

		public Node a;
		private void Start()
		{
			_nodes = new Node[RowCount, ColumnCount];
			for (int x = 0; x < RowCount; x++)
			{
				for (int y = 0; y < ColumnCount; y++)
				{
					_nodes[x, y] = Instantiate(nodePrefab, Vector3.zero, 
						Quaternion.identity, transform);
					_nodes[x, y].transform.localPosition = new Vector3((x * nodeSize) - (nodeSize * RowCount / 2), (y * nodeSize) -
						(nodeSize * ColumnCount / 2), 0);
					_nodes[x, y].x = x;
					_nodes[x, y].y = y;
					_nodes[x, y].Type = tileTypes[Random.Range(0, tileTypes.Length)];
					_nodes[x,y].button.onClick.AddListener(() => Select(_nodes[x,y]));
				}
			}
			

			// if (ensureNoStartingMatches) StartCoroutine(EnsureNoStartingMatches());
			//
			// {
			// 	OnMatch += (type, count) => Debug.Log($"Matched {count}x {type.name}.");
			// }
		}

		private void Update()
		{
			if (Input.GetKeyDown(KeyCode.Space))
			{
				var bestMove = TileDataMatrix.FindBestMove(Matrix);

				if (bestMove != null)
				{
					Select(GetTile(bestMove.X1, bestMove.Y1));
					Select(GetTile(bestMove.X2, bestMove.Y2));
				}
			}
		}

		private IEnumerator EnsureNoStartingMatches()
		{
			var wait = new WaitForEndOfFrame();
			while (TileDataMatrix.FindBestMatch(Matrix) != null)
			{
				Shuffle();
				yield return wait;
			}
		}

		private Node GetTile(int x, int y) => _nodes[x,y];

		private Node[] GetTiles(IList<TileData> tileData)
		{
			var length = tileData.Count;

			var tiles = new Node[length];

			for (var i = 0; i < length; i++) tiles[i] = GetTile(tileData[i].X, tileData[i].Y);

			return tiles;
		}

		private async void Select(Node node)
		{
			if (_isSwapping || _isMatching || _isShuffling) return;

			if (!_selection.Contains(node))
			{
				if (_selection.Count > 0)
				{
					if (Math.Abs(node.x - _selection[0].x) == 1 && Math.Abs(node.y - _selection[0].y) == 0 ||
					    Math.Abs(node.y - _selection[0].y) == 1 && Math.Abs(node.x - _selection[0].x) == 0)
					{
						_selection.Add(node);
					}
				}
				else
				{
					_selection.Add(node);
				}
			}

			if (_selection.Count < 2) return;

			await SwapAsync(_selection[0], _selection[1]);

			if (!await TryMatchAsync()) await SwapAsync(_selection[0], _selection[1]);

			var matrix = Matrix;

			while (TileDataMatrix.FindBestMove(matrix) == null || TileDataMatrix.FindBestMatch(matrix) != null)
			{
				Shuffle();

				matrix = Matrix;
			}

			_selection.Clear();
		}

		private async Task SwapAsync(Node tile1, Node tile2)
		{
			_isSwapping = true;

			var icon1 = tile1.icon;
			var icon2 = tile2.icon;

			var icon1Transform = icon1.transform;
			var icon2Transform = icon2.transform;

			icon1Transform.SetParent(swappingOverlay);
			icon2Transform.SetParent(swappingOverlay);

			icon1Transform.SetAsLastSibling();
			icon2Transform.SetAsLastSibling();

			var sequence = DOTween.Sequence();

			sequence.Join(icon1Transform.DOMove(icon2Transform.position, tweenDuration).SetEase(Ease.OutBack))
			        .Join(icon2Transform.DOMove(icon1Transform.position, tweenDuration).SetEase(Ease.OutBack));

			await sequence.Play()
			              .AsyncWaitForCompletion();

			icon1Transform.SetParent(tile2.transform);
			icon2Transform.SetParent(tile1.transform);

			tile1.icon = icon2;
			tile2.icon = icon1;

			var tile1Item = tile1.Type;

			tile1.Type = tile2.Type;

			tile2.Type = tile1Item;

			_isSwapping = false;
		}

		private async Task<bool> TryMatchAsync()
		{
			var didMatch = false;

			_isMatching = true;

			var match = TileDataMatrix.FindBestMatch(Matrix);

			while (match != null)
			{
				didMatch = true;

				var tiles = GetTiles(match.Tiles);

				var deflateSequence = DOTween.Sequence();

				foreach (var tile in tiles) deflateSequence.Join(tile.icon.transform.DOScale(Vector3.zero, tweenDuration).SetEase(Ease.InBack));

				audioSource.PlayOneShot(matchSound);

				await deflateSequence.Play()
				                     .AsyncWaitForCompletion();

				var inflateSequence = DOTween.Sequence();

				foreach (var tile in tiles)
				{
					tile.Type = tileTypes[Random.Range(0, tileTypes.Length)];

					inflateSequence.Join(tile.icon.transform.DOScale(Vector3.one, tweenDuration).SetEase(Ease.OutBack));
				}

				await inflateSequence.Play()
				                     .AsyncWaitForCompletion();

				OnMatch?.Invoke(Array.Find(tileTypes, tileType => tileType.id == match.TypeId), match.Tiles.Length);

				match = TileDataMatrix.FindBestMatch(Matrix);
			}

			_isMatching = false;

			return didMatch;
		}

		private void Shuffle()
		{
			_isShuffling = true;

			for (int x = 0; x < RowCount; x++)
			{
				for (int y = 0; y < ColumnCount; y++)
				{
					_nodes[x, y].Type = tileTypes[Random.Range(0, tileTypes.Length)];
				}
			}

			_isShuffling = false;
		}
	}
}
