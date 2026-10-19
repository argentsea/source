using System;
using System.Collections.Generic;
using System.IO;
using System.Linq.Expressions;
using FluentAssertions;
using Xunit;

namespace ArgentSea.Test
{
    /// <summary>
    /// Spec section expression-debug-view: golden-string renderings of the vendored <see cref="DebugViewWriter"/> for the
    /// scenarios the spec names (operator precedence parenthesization, the lambda header, the conditional block, New, Call,
    /// RuntimeVariables, constants and multi-element list layout). The writer is excluded from code coverage; these tests pin
    /// only the notation the library's expression-tree log events depend on.
    /// </summary>
    public class DebugViewWriterTests
    {
        public class Foo
        {
            public Foo(int value) => Value = value;
            public int Value { get; }
            public int Method(int y) => y;
        }

        private static readonly ParameterExpression X = Expression.Parameter(typeof(int), "x");
        private static readonly ParameterExpression Y = Expression.Parameter(typeof(int), "y");
        private static readonly ParameterExpression Z = Expression.Parameter(typeof(int), "z");

        private static string Render(Expression node)
        {
            using var writer = new StringWriter { NewLine = "\n" };
            DebugViewWriter.WriteTo(node, writer);
            return writer.ToString();
        }

        private static string Lines(params string[] lines) => string.Join("\n", lines);

        [Fact]
        [Trait("Spec", "blk-bceb92ad4a6947f9a7c035149a4870e0")]
        [Trait("Spec", "blk-1e46588e666949c4bf6be6d042bf06df")]
        public void BinaryOperandsOfHigherPrecedenceAreNotParenthesized()
        {
            Render(Expression.Add(X, Y)).Should().Be("$x + $y");
        }

        [Fact]
        [Trait("Spec", "blk-b6aca07a12544038b7a905aab5d89e61")]
        [Trait("Spec", "blk-1e46588e666949c4bf6be6d042bf06df")]
        public void ALowerPrecedenceOperandIsParenthesized()
        {
            Render(Expression.Multiply(Expression.Add(X, Y), Z)).Should().Be("($x + $y) * $z");
        }

        public static IEnumerable<object[]> ParenthesizationCases()
        {
            // (b) higher-precedence parent: the lower-precedence operand is wrapped; the same-or-higher one is not.
            yield return new object[] { "lower precedence operand under a higher precedence parent", Expression.Multiply(X, Expression.Add(Y, Z)), "$x * ($y + $z)" };
            yield return new object[] { "higher precedence operand under a lower precedence parent", Expression.Add(X, Expression.Multiply(Y, Z)), "$x + $y * $z" };
            // (e) equal precedence under a parent outside the associative set is wrapped (Equal parent, NotEqual operand).
            yield return new object[] { "equal precedence under Equal", Expression.Equal(Expression.NotEqual(X, Y), Expression.Constant(true)), "($x != $y) == True" };
            // Associative parents of equal precedence drop the parentheses.
            yield return new object[] { "equal precedence under Add", Expression.Add(X, Expression.Subtract(Y, Z)), "$x + $y - $z" };
            yield return new object[] { "equal precedence under Multiply", Expression.Multiply(Expression.Divide(X, Y), Z), "$x / $y * $z" };
            yield return new object[] { "AndAlso chain", Expression.AndAlso(Expression.AndAlso(Expression.GreaterThan(X, Y), Expression.LessThan(Y, Z)), Expression.Equal(X, Z)), "$x > $y && $y < $z && $x == $z" };
            // (c) Subtract, Divide and Modulo keep the parentheses only on their right operand.
            yield return new object[] { "Subtract left operand of equal precedence", Expression.Subtract(Expression.Subtract(X, Y), Z), "$x - $y - $z" };
            yield return new object[] { "Subtract right operand of equal precedence", Expression.Subtract(X, Expression.Subtract(Y, Z)), "$x - ($y - $z)" };
            yield return new object[] { "Divide right operand of equal precedence", Expression.Divide(X, Expression.Multiply(Y, Z)), "$x / ($y * $z)" };
            yield return new object[] { "Modulo right operand of equal precedence", Expression.Modulo(X, Expression.Divide(Y, Z)), "$x % ($y / $z)" };
            // (d) a negated constant is parenthesized so it does not read as a negative literal; a negated parameter is not.
            yield return new object[] { "Negate of a constant", Expression.Negate(Expression.Constant(5)), "-(5)" };
            yield return new object[] { "Negate of a parameter", Expression.Negate(X), "-$x" };
            // (a) Increment, Decrement, IsTrue, IsFalse and Unbox always parenthesize their operand.
            yield return new object[] { "Increment", Expression.Increment(X), ".Increment($x)" };
            yield return new object[] { "Decrement", Expression.Decrement(X), ".Decrement($x)" };
            yield return new object[] { "IsTrue", Expression.IsTrue(Expression.Constant(true)), ".IsTrue(True)" };
            yield return new object[] { "Unbox", Expression.Unbox(Expression.Parameter(typeof(object), "o"), typeof(int)), ".Unbox($o)" };
            // A checked operator carries a # prefix.
            yield return new object[] { "AddChecked", Expression.AddChecked(X, Y), "$x #+ $y" };
            // Array index renders as left[right].
            yield return new object[] { "ArrayIndex", Expression.ArrayIndex(Expression.Parameter(typeof(int[]), "a"), X), "$a[$x]" };
        }

        [Theory]
        [MemberData(nameof(ParenthesizationCases))]
        [Trait("Spec", "blk-087089d4ef9d4940a3623d13bd21a497")]
        [Trait("Spec", "blk-1e46588e666949c4bf6be6d042bf06df")]
        public void OperandsAreParenthesizedExactlyWhenThePrecedenceTableRequires(string scenario, Expression node, string expected)
        {
            Render(node).Should().Be(expected, scenario);
        }

        [Fact]
        [Trait("Spec", "blk-1046c3a748994ffcb50a0cdda3d90d27")]
        [Trait("Spec", "blk-a61d1b7131564fa8969944e46ec6199c")]
        public void ConditionalRendersAsAnIfElseBlockWithIndentedBranches()
        {
            var node = Expression.Condition(Expression.Equal(X, Y), Expression.Constant(1), Expression.Constant(2));

            Render(node).Should().Be(Lines(
                ".If ($x == $y) {",
                "    1",
                "} .Else {",
                "    2",
                "}"));
        }

        [Fact]
        [Trait("Spec", "blk-24186a3cb91e46a78fea4bf9542f307b")]
        [Trait("Spec", "blk-6ff6aaeeb0c945a8913394244859c2c9")]
        [Trait("Spec", "blk-a8bf29bcc532445c840e297265c36157")]
        public void ANestedLambdaRendersItsHeaderAndIsDefinedOnceAfterTheRoot()
        {
            var add = Expression.Lambda<Func<int, int, int>>(Expression.Add(X, Y), "Add", new[] { X, Y });
            // The same lambda is invoked twice, so its definition must still be appended exactly once.
            var root = Expression.Add(Expression.Invoke(add, Expression.Constant(1), Expression.Constant(2)), Expression.Invoke(add, Expression.Constant(3), Expression.Constant(4)));

            var rendered = Render(root);

            const string header = ".Lambda Add<System.Func`3[System.Int32,System.Int32,System.Int32]>";
            rendered.Should().StartWith(".Invoke (" + header + ")(", "an invoked lambda operand is parenthesized");
            rendered.Should().Contain(Lines("", "", header + "(", "    System.Int32 $x,", "    System.Int32 $y) {", "    $x + $y", "}"), "the definition follows the root rendering");
            rendered.Split(header + "(").Length.Should().Be(2, "the lambda is defined exactly once");
            rendered.Should().EndWith("}");
        }

        [Fact]
        [Trait("Spec", "blk-a8bf29bcc532445c840e297265c36157")]
        public void ARootLambdaRendersAsItsDefinition()
        {
            var add = Expression.Lambda<Func<int, int, int>>(Expression.Add(X, Y), "Add", new[] { X, Y });

            Render(add).Should().Be(Lines(
                ".Lambda Add<System.Func`3[System.Int32,System.Int32,System.Int32]>(",
                "    System.Int32 $x,",
                "    System.Int32 $y) {",
                "    $x + $y",
                "}"));
        }

        [Fact]
        [Trait("Spec", "blk-567a96448c104b48988b6f3eb55e65c9")]
        [Trait("Spec", "blk-5b9a91bdc246463f93cbac2d1fd4bf63")]
        public void NewRendersTheTypeAndAParenthesizedArgumentList()
        {
            var node = Expression.New(typeof(Foo).GetConstructor(new[] { typeof(int) })!, Expression.Constant(1));

            Render(node).Should().Be($".New {typeof(Foo)}(1)");
        }

        [Fact]
        [Trait("Spec", "blk-fc768f163630450592028a7d6eda162e")]
        [Trait("Spec", "blk-97b4c596c5ae429a885152c80e8f146b")]
        public void InstanceCallRendersTheReceiverMethodAndArguments()
        {
            var obj = Expression.Parameter(typeof(Foo), "obj");
            var node = Expression.Call(obj, typeof(Foo).GetMethod(nameof(Foo.Method))!, Y);

            Render(node).Should().Be(".Call $obj.Method($y)");
        }

        [Fact]
        [Trait("Spec", "blk-97b4c596c5ae429a885152c80e8f146b")]
        public void StaticCallMemberAccessAndInvocationRenderTheirKeywords()
        {
            var obj = Expression.Parameter(typeof(Foo), "obj");
            var staticCall = Expression.Call(typeof(Math).GetMethod(nameof(Math.Abs), new[] { typeof(int) })!, X);
            var member = Expression.Property(obj, nameof(Foo.Value));
            var invoke = Expression.Invoke(Expression.Parameter(typeof(Func<int, int>), "f"), X);

            Render(staticCall).Should().Be(".Call System.Math.Abs($x)");
            Render(member).Should().Be("$obj.Value");
            Render(invoke).Should().Be(".Invoke $f($x)");
        }

        [Fact]
        [Trait("Spec", "blk-f36bafffb8134c43b18db47b4d2050fa")]
        [Trait("Spec", "blk-011b4d48e7394849a03101a29b6c9d3c")]
        public void RuntimeVariablesRendersItsVariableList()
        {
            Render(Expression.RuntimeVariables(X)).Should().Be(".RuntimeVariables($x)");
        }

        [Fact]
        [Trait("Spec", "blk-555b5c8a9bac484d9c62012afcaae775")]
        [Trait("Spec", "blk-6ff6aaeeb0c945a8913394244859c2c9")]
        public void ConstantsRenderAsLiteralsWithTypeSuffixesOrAConstantWrapper()
        {
            Render(Expression.Constant(5)).Should().Be("5");
            Render(Expression.Constant(true)).Should().Be("True");
            Render(Expression.Constant("hi")).Should().Be("\"hi\"");
            Render(Expression.Constant('c')).Should().Be("'c'");
            Render(Expression.Constant(5u)).Should().Be("5U");
            Render(Expression.Constant(5L)).Should().Be("5L");
            Render(Expression.Constant(5UL)).Should().Be("5UL");
            Render(Expression.Constant(2.5)).Should().Be("2.5D");
            Render(Expression.Constant(2.5f)).Should().Be("2.5F");
            Render(Expression.Constant(2.5m)).Should().Be("2.5M");
            Render(Expression.Constant(null, typeof(string))).Should().Be("null");
            Render(Expression.Constant((short)5)).Should().Be(".Constant<System.Int16>(5)");
            Render(Expression.Default(typeof(int))).Should().Be(".Default(System.Int32)");
        }

        [Fact]
        [Trait("Spec", "blk-6ff6aaeeb0c945a8913394244859c2c9")]
        public void UnnamedParametersGetSequentialSyntheticNames()
        {
            var first = Expression.Parameter(typeof(int));
            var second = Expression.Parameter(typeof(int));

            Render(Expression.Add(Expression.Add(first, second), first)).Should().Be("$var1 + $var2 + $var1");
        }

        [Fact]
        [Trait("Spec", "blk-3b8abf5e386e4a38a5143b19cb5e974b")]
        [Trait("Spec", "blk-10afcd7c961e4d2abf22f5028f013a15")]
        public void AMultiElementListBreaksEachElementOntoItsOwnIndentedLine()
        {
            var a = Expression.Parameter(typeof(int), "a");
            var b = Expression.Parameter(typeof(int), "b");
            var node = Expression.Call(typeof(Math).GetMethod(nameof(Math.Max), new[] { typeof(int), typeof(int) })!, a, b);

            Render(node).Should().Be(Lines(
                ".Call System.Math.Max(",
                "    $a,",
                "    $b)"));
        }

        [Fact]
        [Trait("Spec", "blk-10afcd7c961e4d2abf22f5028f013a15")]
        [Trait("Spec", "blk-5b9a91bdc246463f93cbac2d1fd4bf63")]
        public void ABracedListAlwaysBreaksAndClosesOnItsOwnLine()
        {
            var one = Expression.NewArrayInit(typeof(int), Expression.Constant(1));
            var two = Expression.NewArrayInit(typeof(int), Expression.Constant(1), Expression.Constant(2));
            var bounds = Expression.NewArrayBounds(typeof(int), Expression.Constant(3));

            Render(one).Should().Be(Lines(".NewArray System.Int32[] {", "    1", "}"), "a braced list breaks even with one element");
            Render(two).Should().Be(Lines(".NewArray System.Int32[] {", "    1,", "    2", "}"));
            Render(bounds).Should().Be(".NewArray System.Int32[3]", "a single-element bracketed list stays on one line");
        }

        [Fact]
        [Trait("Spec", "blk-a61d1b7131564fa8969944e46ec6199c")]
        [Trait("Spec", "blk-10afcd7c961e4d2abf22f5028f013a15")]
        public void ABlockRendersItsTypeDeclarationsAndSemicolonSeparatedBody()
        {
            var node = Expression.Block(typeof(int), new[] { X }, Expression.Assign(X, Expression.Constant(1)), X);

            Render(node).Should().Be(Lines(
                ".Block<System.Int32>(System.Int32 $x) {",
                "    $x = 1;",
                "    $x",
                "}"));
        }
    }
}
