// "Therefore those skilled at the unorthodox
// are infinite as heaven and earth,
// inexhaustible as the great rivers.
// When they come to an end,
// they begin again,
// like the days and months;
// they die and are reborn,
// like the four seasons."
// 
// - Sun Tsu,
// "The Art of War"

using System;
using System.Collections.Generic;
using TheArtOfDev.HtmlRenderer.Core.Dom;
using TheArtOfDev.HtmlRenderer.Core.Utils;

namespace TheArtOfDev.HtmlRenderer.Core.Parse {
    /// <summary>
    /// 
    /// </summary>
    internal static class HtmlParser {
        /// <summary>
        /// Parses the source html to css boxes tree structure.
        /// </summary>
        /// <param name="source">the html source to parse</param>
        public static CssBox ParseDocument(string source) {
            // 生成树根节点
            var root = CssBox.CreateBlock();
            var curBox = root;

            int endIdx = 0;
            int startIdx = 0;
            while (startIdx >= 0) {
                var tagIdx = source.IndexOf('<', startIdx);
                if (tagIdx >= 0 && tagIdx < source.Length) {
                    // add the html text as anon css box to the structure
                    // 将 html 文本作为匿名的 CssBox 添加到父 CssBox 中。
                    // 分几种情况，第一次从文本最开始的位置计算，如 <!DOCTYPE html>
                    // 对于这种情况，开始 startIdx == tagIdx，所以 AddTextBox 中不会添加任何文本。
                    // 对于嵌套标签，如 <p>The <code>foreach</code> loop in this example</p>
                    // 假设 startIdx 指向第一个单词 The 的首字母 T，tagIdx 指向 <code> 的第一个字符 <，
                    // 那么要添加的文本就是 "The "；
                    // 对于非嵌套标签，如 <p>The foreach loop in this example</p>，将会提取到 p 标签内的所有文本。
                    // 提取到文本后，创建一个新的 CssBox，然后将它添加到 curBox 并保存文本。
                    // 注意，因为之前已经通过 ParseHtmlTag 解析了 p 标签，所以 curBox
                    // 当前已经指向了文本所在标签（示例中是 p）的 CssBox，不再指向 root 节点。
                    AddTextBox(source, startIdx, tagIdx, ref curBox);

                    if (source[tagIdx + 1] == '!') {
                        if (source[tagIdx + 2] == '-') {
                            // skip the html comment elements (<!-- bla -->)
                            startIdx = source.IndexOf("-->", tagIdx + 2);
                            endIdx = startIdx > 0 ? startIdx + 3 : tagIdx + 2;
                        } else {
                            // skip the html crap elements (<!crap bla>)
                            //  <!DOCTYPE html> 此类元素会被跳过
                            startIdx = source.IndexOf(">", tagIdx + 2);
                            endIdx = startIdx > 0 ? startIdx + 1 : tagIdx + 2;
                        }
                        // 如果要处理 EPUB 的文件，考虑加上对 ? 的判断，如 <?xml version="1.0" encoding="UTF-8"?>
                        // 这种情况下如果不忽略 ？，则在下面 ParseHtmlTag 中解析得到的标签会是 ?xml，并且会导致后续的 html
                        // 元素都成为它的子元素。或者在应用层把该元素移除后再交到这边解析剩下的部分。
                    } else {
                        // parse element tag to css box structure
                        endIdx = ParseHtmlTag(source, tagIdx, ref curBox) + 1;

                        if (curBox.HtmlTag != null && curBox.HtmlTag.Name.Equals(HtmlConstants.Style, StringComparison.OrdinalIgnoreCase)) {
                            var endIdxS = endIdx;
                            endIdx = source.IndexOf("</style>", endIdx, StringComparison.OrdinalIgnoreCase);
                            if (endIdx > -1)
                                AddTextBox(source, endIdxS, endIdx, ref curBox);
                        }
                    }
                }
                startIdx = tagIdx > -1 && endIdx > 0 ? endIdx : -1;
            }

            // handle pieces of html without proper structure
            if (endIdx > -1 && endIdx < source.Length) {
                // there is text after the end of last element
                var endText = new SubString(source, endIdx, source.Length - endIdx);
                if (!endText.IsEmptyOrWhitespace()) {
                    var abox = CssBox.CreateBox(root);
                    abox.Text = endText;
                }
            }

            return root;
        }


        #region Private methods

        /// <summary>
        /// Add html text anon box to the current box, this box will have the rendered text<br/>
        /// Adding box also for text that contains only whitespaces because we don't know yet if
        /// the box is preformatted. At later stage they will be removed if not relevant.
        /// </summary>
        /// <param name="source">the html source to parse</param>
        /// <param name="startIdx">the start of the html part</param>
        /// <param name="tagIdx">the index of the next html tag</param>
        /// <param name="curBox">the current box in html tree parsing。此参数不应该用 ref，因为没有改变引用</param>
        private static void AddTextBox(string source, int startIdx, int tagIdx, ref CssBox curBox) {
            var text = tagIdx > startIdx ? new SubString(source, startIdx, tagIdx - startIdx) : null;
            if (text != null) {
                var abox = CssBox.CreateBox(curBox);
                abox.Text = text;
            }
        }

        /// <summary>
        /// Parse the html part, the part from prev parsing index to the beginning of the next html tag.<br/>
        /// </summary>
        /// <param name="source">the html source to parse</param>
        /// <param name="tagIdx">the index of the next html tag</param>
        /// <param name="curBox">the current box in html tree parsing</param>
        /// <returns>the end of the parsed part, the new start index</returns>
        private static int ParseHtmlTag(string source, int tagIdx, ref CssBox curBox) {
            var endIdx = source.IndexOf('>', tagIdx + 1);
            if (endIdx > 0) {
                string tagName;
                Dictionary<string, string> tagAttributes;
                var length = endIdx - tagIdx + 1 - (source[endIdx - 1] == '/' ? 1 : 0);
                if (ParseHtmlTag(source, tagIdx, length, out tagName, out tagAttributes)) {
                    // 当前是结束标签
                    if (!HtmlUtils.IsSingleTag(tagName) && curBox.ParentBox != null) {
                        // need to find the parent tag to go one level up
                        // 代表一个标签结束了，没有嵌套内容了，所以需要找到与之名字相匹配的开始标签的父 CssBox。
                        curBox = DomUtils.FindParent(curBox.ParentBox, tagName, curBox);
                    }
                } else if (!string.IsNullOrEmpty(tagName)) {
                    // 当前是开始标签
                    //new SubString(source, lastEnd + 1, tagmatch.Index - lastEnd - 1)
                    var isSingle = HtmlUtils.IsSingleTag(tagName) || source[endIdx - 1] == '/';
                    var tag = new HtmlTag(tagName, isSingle, tagAttributes);

                    if (isSingle) {
                        // the current box is not changed
                        // 单标签时只是把 CreateBox 新生成的 CssBox 最为儿子节点挂在当前 curBox 下
                        CssBox.CreateBox(tag, curBox);
                    } else {
                        // go one level down, make the new box the current box
                        // 非单标签，CreateBox 新生成的 CssBox 除了作为子节点外，还要把当前的 curBox 引用指向它。
                        curBox = CssBox.CreateBox(tag, curBox);
                    }
                } else {
                    endIdx = tagIdx + 1;
                }
            }
            return endIdx;
        }

        /// <summary>
        /// Parse raw html tag source to <seealso cref="HtmlTag"/> object.<br/>
        /// Extract attributes found on the tag.
        /// </summary>
        /// <param name="source">the html source to parse</param>
        /// <param name="idx">the start index of the tag in the source</param>
        /// <param name="length">the length of the tag from the start index in the source</param>
        /// <param name="name">return the name of the html tag</param>
        /// <param name="attributes">return the dictionary of tag attributes</param>
        /// <returns>true - the tag is closing tag, false - otherwise</returns>
        private static bool ParseHtmlTag(string source, int idx, int length, out string name, out Dictionary<string, string> attributes) {
            idx++;
            length = length - (source[idx + length - 3] == '/' ? 3 : 2);

            // Check if is end tag
            var isClosing = false;
            if (source[idx] == '/') {
                idx++;
                length--;
                isClosing = true;
            }

            int spaceIdx = idx;
            while (spaceIdx < idx + length && !char.IsWhiteSpace(source, spaceIdx))
                spaceIdx++;

            // Get the name of the tag
            name = source.Substring(idx, spaceIdx - idx).ToLower();

            attributes = null;
            if (!isClosing && idx + length > spaceIdx) {
                ExtractAttributes(source, spaceIdx, length - (spaceIdx - idx), out attributes);
            }

            return isClosing;
        }

        /// <summary>
        /// Extract html tag attributes from the given sub-string.
        /// </summary>
        /// <param name="source">the html source to parse</param>
        /// <param name="idx">the start index of the tag attributes in the source</param>
        /// <param name="length">the length of the tag attributes from the start index in the source</param>
        /// <param name="attributes">return the dictionary of tag attributes</param>
        private static void ExtractAttributes(string source, int idx, int length, out Dictionary<string, string> attributes) {
            attributes = null;

            int startIdx = idx;
            while (startIdx < idx + length) {
                while (startIdx < idx + length && char.IsWhiteSpace(source, startIdx))
                    startIdx++;

                var endIdx = startIdx + 1;
                while (endIdx < idx + length && !char.IsWhiteSpace(source, endIdx) && source[endIdx] != '=')
                    endIdx++;

                if (startIdx < idx + length) {
                    var key = source.Substring(startIdx, endIdx - startIdx);
                    var value = "";

                    startIdx = endIdx + 1;
                    while (startIdx < idx + length && (char.IsWhiteSpace(source, startIdx) || source[startIdx] == '='))
                        startIdx++;

                    bool hasPChar = false;
                    if (startIdx < idx + length) {
                        char pChar = source[startIdx];
                        if (pChar == '"' || pChar == '\'') {
                            hasPChar = true;
                            startIdx++;
                        }

                        endIdx = startIdx + (hasPChar ? 0 : 1);
                        while (endIdx < idx + length && (hasPChar ? source[endIdx] != pChar : !char.IsWhiteSpace(source, endIdx)))
                            endIdx++;

                        value = source.Substring(startIdx, endIdx - startIdx);
                        value = HtmlUtils.DecodeHtml(value);
                    }

                    if (key.Length != 0) {
                        if (attributes == null)
                            attributes = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);
                        attributes[key.ToLower()] = value;
                    }

                    startIdx = endIdx + (hasPChar ? 2 : 1);
                }
            }
        }

        #endregion
    }
}