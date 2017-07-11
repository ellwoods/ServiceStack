using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using ServiceStack.Templates;
using ServiceStack.Text;
using ServiceStack.VirtualPath;

namespace ServiceStack.WebHost.Endpoints.Tests
{
    public class TemplatePageRawTests
    {
        [Test]
        public async Task Can_generate_html_template_with_layout_in_memory()
        {
            var context = new TemplatePagesContext();

            context.VirtualFiles.WriteFile("_layout.html", @"
<html>
  <title>{{ title }}</title>
</head>
<body>
  {{ page }}
</body>");

            context.VirtualFiles.WriteFile("page.html", @"<h1>{{ title }}</h1>");

            var page = context.GetPage("page");
            var result = new PageResult(page)
            {
                Args =
                {
                    {"title", "The Title"},
                }
            };

            var html = await result.RenderToStringAsync();
            
            Assert.That(html, Is.EqualTo(@"<html>
  <title>The Title</title>
</head>
<body>
  <h1>The Title</h1>
</body>"));
        }

        [Test]
        public async Task Can_generate_markdown_template_with_layout_in_memory()
        {
            var context = new TemplatePagesContext
            {
                PageFormats =
                {
                    new MarkdownPageFormat()
                }
            };
            
            context.VirtualFiles.WriteFile("_layout.md", @"
# {{ title }}

Brackets in Layout < & > 

{{ page }}");

            context.VirtualFiles.WriteFile("page.md",  @"## {{ title }}");

            var page = context.GetPage("page");
            var result = new PageResult(page)
            {
                Args =
                {
                    {"title", "The Title"},
                },
                ContentType = MimeTypes.Html,
                OutputFilters = { MarkdownPageFormat.TransformToHtml },
            };

            var html = await result.RenderToStringAsync();
            
            Assert.That(html.SanitizeNewLines(), Is.EqualTo(@"<h1>The Title</h1>
<p>Brackets in Layout &lt; &amp; &gt; </p>
<h2>The Title</h2>".SanitizeNewLines()));
            
        }

        [Test]
        public async Task Can_generate_markdown_template_with_html_layout_in_memory()
        {
            var context = new TemplatePagesContext
            {
                PageFormats =
                {
                    new MarkdownPageFormat()
                }
            };
            
            context.VirtualFiles.WriteFile("_layout.html", @"
<html>
  <title>{{ title }}</title>
</head>
<body>
  {{ page }}
</body>");

            context.VirtualFiles.WriteFile("page.md",  @"### {{ title }}");

            var page = context.GetPage("page");
            var result = new PageResult(page)
            {
                Args =
                {
                    {"title", "The Title"},
                },
                ContentType = MimeTypes.Html,
                PageFilters = { MarkdownPageFormat.TransformToHtml },
            };

            var html = await result.RenderToStringAsync();
            
            Assert.That(html.SanitizeNewLines(), Is.EqualTo(@"<html>
  <title>The Title</title>
</head>
<body>
  <h3>The Title</h3>

</body>".SanitizeNewLines()));
        }

        [Test]
        public async Task Does_explode_Model_properties_into_scope()
        {
            var context = new TemplatePagesContext();
            
            context.VirtualFiles.WriteFile("page.html", @"Id: {{ Id }}, Name: {{ Name }}");
            
            var result = await new PageResult(context.GetPage("page"))
            {
                Model = new Model { Id = 1, Name = "<foo>" }
            }.RenderToStringAsync();
            
            Assert.That(result, Is.EqualTo("Id: 1, Name: &lt;foo&gt;"));
        }

        [Test]
        public async Task Does_explode_Model_properties_of_anon_object_into_scope()
        {
            var context = new TemplatePagesContext();
            
            context.VirtualFiles.WriteFile("page.html", @"Id: {{ Id }}, Name: {{ Name }}");
            
            var result = await new PageResult(context.GetPage("page"))
            {
                Model = new { Id = 1, Name = "<foo>" }
            }.RenderToStringAsync();
            
            Assert.That(result, Is.EqualTo("Id: 1, Name: &lt;foo&gt;"));
        }

        [Test]
        public async Task Does_reload_modified_page_contents_in_DebugMode()
        {
            var context = new TemplatePagesContext
            {
                DebugMode = true, //default
            };
            
            context.VirtualFiles.WriteFile("page.html", "<h1>Original</h1>");
            Assert.That(await new PageResult(context.GetPage("page")).RenderToStringAsync(), Is.EqualTo("<h1>Original</h1>"));

            await Task.Delay(1); //Memory VFS is too fast!
            
            context.VirtualFiles.WriteFile("page.html", "<h1>Updated</h1>");
            Assert.That(await new PageResult(context.GetPage("page")).RenderToStringAsync(), Is.EqualTo("<h1>Updated</h1>"));
        }

        [Test]
        public void Context_Throws_FileNotFoundException_when_page_does_not_exist()
        {
            var context = new TemplatePagesContext();

            Assert.That(context.Pages.GetPage("not-exists.html"), Is.Null);

            try
            {
                var page = context.GetPage("not-exists.html");
                Assert.Fail("Should throw");
            }
            catch (FileNotFoundException e)
            {
                e.ToString().Print();
            }
        }

        class MyFilter : TemplateFilter
        {
            public string echo(string text) => $"{text} {text}";
            public string greetArg(string key) => $"Hello {Context.Args[key]}";
        }

        [Test]
        public async Task Does_use_custom_filter()
        {
            var context = new TemplatePagesContext
            {
                Args =
                {
                    ["contextArg"] = "foo"
                },                
            }.Init();
            
            context.VirtualFiles.WriteFile("page.html", "<h1>{{ 'hello' | echo }}</h1>");
            var result = await new PageResult(context.GetPage("page"))
            {
                TemplateFilters = { new MyFilter() }
            }.RenderToStringAsync();
            Assert.That(result, Is.EqualTo("<h1>hello hello</h1>"));

            context.VirtualFiles.WriteFile("page-greet.html", "<h1>{{ 'contextArg' | greetArg }}</h1>");
            result = await new PageResult(context.GetPage("page-greet"))
            {
                TemplateFilters = { new MyFilter() }
            }.RenderToStringAsync();
            Assert.That(result, Is.EqualTo("<h1>Hello foo</h1>"));
        }

        [Test]
        public async Task Does_embed_pages()
        {
            var context = new TemplatePagesContext
            {
                Args =
                {
                    ["copyright"] = "Copyright &copy; ServiceStack 2008-2017",
                    ["footer"] = "global-footer"
                }
            }.Init();
            
            context.VirtualFiles.WriteFile("_layout.html", @"
<html>
<head><title>{{ title }}</title></head>
<body>
{{ 'header' | page }}
<div id='content'>{{ page }}</div>
{{ footer | page }}
</body>
</html>
");
            context.VirtualFiles.WriteFile("header.html", "<header>{{ pageTitle | titleCase }}</header>");
            context.VirtualFiles.WriteFile("page.html", "<h2>{{ contentTitle }}</h2><section>{{ 'page-content' | page }}</section>");
            context.VirtualFiles.WriteFile("page-content.html", "<p>{{ contentBody | padRight(20,'.') }}</p>");
            context.VirtualFiles.WriteFile("global-footer.html", "<footer>{{ copyright | raw }}</footer>");
            
            var result = await new PageResult(context.GetPage("page"))
            {
                Args =
                {
                    ["pageTitle"] = "I'm in your header",
                    ["contentTitle"] = "Content is King!",
                    ["contentBody"] = "About this page",
                }
            }.RenderToStringAsync();
            
            Assert.That(result.SanitizeNewLines(), Is.EqualTo(@"
<html>
<head><title>{{ title }}</title></head>
<body>
<header>I&#39;m In Your Header</header>
<div id='content'><h2>Content is King!</h2><section><p>About this page.....</p></section></div>
<footer>Copyright &copy; ServiceStack 2008-2017</footer>
</body>
</html>
".SanitizeNewLines()));
        }

        public class ModelBinding
        {
            public int Int { get; set;  }
            
            public string Prop { get; set; }
            
            public NestedModelBinding Object { get; set; }
            
            public Dictionary<string, ModelBinding> Dictionary { get; set; }
            
            public List<ModelBinding> List { get; set; }
            
            public ModelBinding this[int i]
            {
                get => List[i];
                set => List[i] = value;
            }
        }
        
        public class NestedModelBinding
        {
            public int Int { get; set;  }
            
            public string Prop { get; set; }
            
            public ModelBinding Object { get; set; }
            
            public AltNested AltNested { get; set; }
            
            public Dictionary<string, ModelBinding> Dictionary { get; set; }
            
            public List<ModelBinding> List { get; set; }
        }
        
        public class AltNested
        {
            public string Field { get; set; }
        }


        private static ModelBinding CreateModelBinding()
        {
            var model = new ModelBinding
            {
                Int = 1,
                Prop = "The Prop",
                Object = new NestedModelBinding
                {
                    Int = 2,
                    Prop = "Nested Prop",
                    Object = new ModelBinding
                    {
                        Int = 21,
                        Prop = "Nested Nested Prop",
                    },
                    AltNested = new AltNested
                    {
                        Field = "Object AltNested Field"
                    }
                },
                Dictionary = new Dictionary<string, ModelBinding>
                {
                    {
                        "map-key",
                        new ModelBinding
                        {
                            Int = 3,
                            Prop = "Dictionary Prop",
                            Object = new NestedModelBinding
                            {
                                Int = 5,
                                Prop = "Nested Dictionary Prop",
                                AltNested = new AltNested
                                {
                                    Field = "Dictionary AltNested Field"
                                }
                            }
                        }
                    },
                },
                List = new List<ModelBinding>
                {
                    new ModelBinding
                    {
                        Int = 4,
                        Prop = "List Prop",
                        Object = new NestedModelBinding {Int = 5, Prop = "Nested List Prop"}
                    }
                }
            };
            return model;
        }

        [Test]
        public async Task Does_evaluate_variable_binding_expressions()
        {
            var context = new TemplatePagesContext
            {
                Args =
                {
                    ["key"] = "the-key",
                }
            }.Init();
            
            context.VirtualFiles.WriteFile("page.html", @"Prop = {{ Prop }}");

            var model = CreateModelBinding();

            var pageResultArg = new NestedModelBinding
            {
                Int = 2,
                Prop = "Nested Prop",
                Object = new ModelBinding
                {
                    Int = 21,
                    Prop = "Nested Nested Prop",
                },
                AltNested = new AltNested
                {
                    Field = "Object AltNested Field"
                }
            };
            
            var result = await new PageResult(context.GetPage("page"))
            {
                Model = model,
                Args = { ["pageResultArg"] = pageResultArg }
            }.Init();

            object value;

            value = result.EvaluateBinding("key");
            Assert.That(value, Is.EqualTo("the-key"));
            value = result.EvaluateBinding("Prop");
            Assert.That(value, Is.EqualTo(model.Prop));

            value = result.EvaluateBinding("model.Prop");
            Assert.That(value, Is.EqualTo(model.Prop));
            value = result.EvaluateBinding("model.Object.Prop");
            Assert.That(value, Is.EqualTo(model.Object.Prop));
            value = result.EvaluateBinding("model.Object.Object.Prop");
            Assert.That(value, Is.EqualTo(model.Object.Object.Prop));
            value = result.EvaluateBinding("model.Object.AltNested.Field");
            Assert.That(value, Is.EqualTo(model.Object.AltNested.Field));
            value = result.EvaluateBinding("model[0].Prop");
            Assert.That(value, Is.EqualTo(model[0].Prop));
            value = result.EvaluateBinding("model[0].Object.Prop");
            Assert.That(value, Is.EqualTo(model[0].Object.Prop));
            value = result.EvaluateBinding("model.List[0]");
            Assert.That(value, Is.EqualTo(model.List[0]));
            value = result.EvaluateBinding("model.List[0].Prop");
            Assert.That(value, Is.EqualTo(model.List[0].Prop));
            value = result.EvaluateBinding("model.List[0].Object.Prop");
            Assert.That(value, Is.EqualTo(model.List[0].Object.Prop));
            value = result.EvaluateBinding("model.Dictionary[\"map-key\"].Prop");
            Assert.That(value, Is.EqualTo(model.Dictionary["map-key"].Prop));
            value = result.EvaluateBinding("model.Dictionary['map-key'].Object.Prop");
            Assert.That(value, Is.EqualTo(model.Dictionary["map-key"].Object.Prop));
            value = result.EvaluateBinding("model.Dictionary['map-key'].Object.AltNested.Field");
            Assert.That(value, Is.EqualTo(model.Dictionary["map-key"].Object.AltNested.Field));
            value = result.EvaluateBinding("Object.AltNested.Field");
            Assert.That(value, Is.EqualTo(model.Object.AltNested.Field));
            
            value = result.EvaluateBinding("pageResultArg.Object.Prop");
            Assert.That(value, Is.EqualTo(pageResultArg.Object.Prop));
            value = result.EvaluateBinding("pageResultArg.AltNested.Field");
            Assert.That(value, Is.EqualTo(pageResultArg.AltNested.Field));
        }

        [Test]
        public async Task Does_evaluate_variable_binding_expressions_in_template()
        {
            var context = new TemplatePagesContext
            {
                Args =
                {
                    ["key"] = "the-key",
                }
            }.Init();
            
            context.VirtualFiles.WriteFile("page.html", @"
Object.Object.Prop = '{{ Object.Object.Prop }}'
model.Object.Object.Prop = '{{ model.Object.Object.Prop }}'
model.Dictionary['map-key'].Object.AltNested.Field = '{{ model.Dictionary['map-key'].Object.AltNested.Field }}'
model.Dictionary['map-key'].Object.AltNested.Field | lower = '{{ model.Dictionary['map-key'].Object.AltNested.Field | lower }}'
");

            var model = CreateModelBinding();
            
            var result = await new PageResult(context.GetPage("page")) { Model = model }.RenderToStringAsync();
            
            Assert.That(result.SanitizeNewLines(), Is.EqualTo(@"
Object.Object.Prop = 'Nested Nested Prop'
model.Object.Object.Prop = 'Nested Nested Prop'
model.Dictionary['map-key'].Object.AltNested.Field = 'Dictionary AltNested Field'
model.Dictionary['map-key'].Object.AltNested.Field | lower = 'dictionary altnested field'
".SanitizeNewLines()));
        }


//#if NET45
//        [Test]
//        public void DumpExpr()
//        {
//            Expression<Func<object, object>> fn = (o) => ((ModelBinding)o).Dictionary["map-key"].Prop;
//            GetDebugView(fn).Print();
//        }
//        
//        public static string GetDebugView(Expression exp)
//        {
//            var propertyInfo = typeof(Expression).GetProperty("DebugView", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
//            return propertyInfo.GetValue(exp) as string;
//        }
//#endif
        
    }
    
    public static class TestUtils
    {
        public static string SanitizeNewLines(this string text) => text.Trim().Replace("\r", "");
    }
}