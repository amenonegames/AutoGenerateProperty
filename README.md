# AutoGenerateProperty

メンバ変数からプロパティを自動実装するSource Generatorを作りました！
具体的には、以下のコードが、
```csharp
        [SerializeField]
        private float _brabra;
        public float Brabra
        {
            get => _brabra;
            private set => _brabra = value;   
        }
```

次のように書けるようになります！
```csharp
        [AutoProp(AXS.PublicGetPrivateSet),SerializeField]
        private float _brabra;
```

詳細な利用方法は以下記事を参照してください！

https://qiita.com/amenone_games/private/25c857608af0138eb646
