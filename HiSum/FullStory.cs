﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Policy;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NLTKSharp;

namespace HiSum
{
    public class FullStory
    {
        public int id { get; set; }
        [JsonIgnore]
        public DateTime created_at { get; set; }
        public string author { get; set; }
        public string title { get; set; }
        public string url { get; set; }
        public string text { get; set; }
        [JsonIgnore]
        public int point { get; set; }
        [JsonIgnore]
        public int? parent_id { get; set; }
        public List<children> children { get; set; }
        string[] stopWords = { "he", "his", "which", "want", "do", "would", "more", "like", "you", "your", "very", "me", "get", "has", "i", "over", "could", "have", "what", "a", "an", "and", "are", "as", "at", "be", "but", "by", "for", "if", "in", "into", "is", "it", "no", "not", "of", "on", "or", "such", "that", "the", "their", "then", "there", "these", "they", "this", "to", "was", "will", "with" };

        public Dictionary<string, HashSet<int>> WordIDMapping
        {
            get { return GetWordIDMapping(); }
        }

        

        public int TotalComments
        {
            get { 
                int count = GetStoryCommentCount();
                return count;
            }
        }

        /*
         * This method sentence-tokenizes all top level comments
         * The best sentences are those where the words in the sentence
         * occur in the most number of subtree items within the current
         * top level comment
         */
        public List<string> GetTopSentences(int N)
        {
            List<string> topSentences = new List<string>();
            Dictionary<string,double> sentenceScores = new Dictionary<string, double>();
            foreach (children child in children)
            {
                Dictionary<string, HashSet<int>> wordIDMapping = GetWordIDMapping(child);
                string text = child.text;
                List<string> currSentences = SentenceTokenizer.Tokenize(Util.StripTagsCharArray(text));
                string bestSentence = currSentences[0];
                double currMax = double.MinValue;
                foreach (string sentence in currSentences)
                {
                    
                    string[] allWords = GetAllWords(sentence);
                    bool goodSentence = (allWords.Length>2)&& (stopWords.Where(x => !allWords.Contains(x.ToLower())).Count() > 2);
                    if (goodSentence)
                    {
                        int totalIDCount = 0;
                        foreach (string word in allWords)
                        {
                            if (!stopWords.Contains(word.ToLower()))
                            {
                                HashSet<int> idsContainingWord = wordIDMapping[Stemmer.GetStem(word)];
                                totalIDCount += idsContainingWord.Count;
                            }
                        }
                        double avgScore = (totalIDCount * 1.0) / allWords.Length;
                        if (avgScore > currMax)
                        {
                            currMax = avgScore;
                            bestSentence = sentence;
                        }
                    }
                }
                sentenceScores[bestSentence] = currMax;
            }
            topSentences = sentenceScores.OrderByDescending(x => x.Value).Take(N).Select(x=>x.Key).ToList();
            return topSentences;
        }

        Dictionary<string, HashSet<int>> GetWordIDMapping()
        {
            Dictionary<string, HashSet<int>> wordIDMapping = new Dictionary<string, HashSet<int>>();
            foreach (children child in children)
            {
                Dictionary<string, HashSet<int>> mapping = GetWordIDMapping(child);
                foreach (var kvp in mapping)
                {
                    if (wordIDMapping.ContainsKey(kvp.Key))
                    {
                        wordIDMapping[kvp.Key].UnionWith(kvp.Value);
                    }
                    else
                    {
                        wordIDMapping[kvp.Key] = kvp.Value;
                    }
                }
            }
            return wordIDMapping;
        }

        Dictionary<string, HashSet<int>> GetWordIDMapping(children child)
        {
            Dictionary<string, HashSet<int>> wordIDMapping = new Dictionary<string, HashSet<int>>();
            string[] allWords = GetAllWords(child.text);
            foreach (string word in allWords)
            {
                string stem = Stemmer.GetStem(word);
                if (!wordIDMapping.ContainsKey(stem))
                {
                    wordIDMapping[stem] = new HashSet<int>();
                }
                wordIDMapping[stem].Add(child.id);
            }
            foreach (children childitem in child.Children)
            {
                Dictionary<string, HashSet<int>> mapping = GetWordIDMapping(childitem);
                foreach (var kvp in mapping)
                {
                    if (wordIDMapping.ContainsKey(kvp.Key))
                    {
                        wordIDMapping[kvp.Key].UnionWith(kvp.Value);
                    }
                    else
                    {
                        wordIDMapping[kvp.Key] = kvp.Value;
                    }
                }
            }
            return wordIDMapping;
        }

        public int GetChildCount(children childrenlist)
        {
            int counter = string.IsNullOrWhiteSpace(childrenlist.text)?0:1;
            foreach (children child in childrenlist.Children)
            {
                counter += GetChildCount(child);
            }
            return counter;
        }

        public int GetStoryCommentCount()
        {
            int counter = 0;
            foreach (children child in this.children)
            {
                int childcount = GetChildCount(child);
                counter += childcount;
            }
            return counter;
        }

        public string GetCommentTree()
        {
            string commentTree = string.Empty;
            commentTree = JsonConvert.SerializeObject(this);
            return commentTree;
        }

        public string GetTagCloudTree()
        {
            FullStory fs = this;
            Dictionary<string, int> topNWordsRoot = fs.GetTopNWordsDictionary(10);
            TagCloudNode tgnRoot = new TagCloudNode();
            tgnRoot.id = fs.id;
            tgnRoot.text = GetTagCloudFromDictionary(topNWordsRoot);
            tgnRoot.children = new List<TagCloudNode>();
            foreach (children child in fs.children)
            {
                TagCloudNode tgnChild = GetTagCloudTree(child);
                tgnRoot.children.Add(tgnChild);
            }
            return JsonConvert.SerializeObject(tgnRoot);
        }

        TagCloudNode GetTagCloudTree(children children)
        {
            Dictionary<string, int> topNWordsRoot = children.GetTopNWordsDictionary(10);
            TagCloudNode tgnRoot = new TagCloudNode();
            tgnRoot.id = children.id;
            tgnRoot.text = GetTagCloudFromDictionary(topNWordsRoot);
            tgnRoot.children = new List<TagCloudNode>();
            foreach (children child in children.Children)
            {
                TagCloudNode tgnChild = GetTagCloudTree(child);
                tgnRoot.children.Add(tgnChild);
            }
            return tgnRoot;
        }

        string GetTagCloudFromDictionary(Dictionary<string, int> dict)
        {
            StringBuilder sb = new StringBuilder();
            foreach (var item in dict)
            {
                double fontSize = ((Math.Min(item.Value, 10) + 5) * 100) / 10;
                sb.Append("<span style='font-size:");
                sb.Append(fontSize);
                sb.Append("%'>");
                sb.Append(item.Key);
                sb.Append("</span>&nbsp;");
            }
            return sb.ToString();
        }

        string[] GetAllWords(string text)
        {
            string tagLess = Util.StripTagsCharArray(text);
            string urlLess = Regex.Replace(tagLess,
                @"((http|ftp|https):\/\/[\w\-_]+(\.[\w\-_]+)+([\w\-\.,@?^=%&amp;:/~\+#]*[\w\-\@?^=%&amp;/~\+#])?)",
                string.Empty);
            string[] separators = { " ", ".", ",", ";", "-", "(", ")", "[", "]", "*", "#", "$", "%", "\"","?","!",":" };
            string[] allWords = urlLess.Split(separators, StringSplitOptions.RemoveEmptyEntries);
            return allWords;
        }

        public Dictionary<string, int> GetTopNWordsDictionary(int N)
        {
            string[] ignoreWords = {"*"};
            
            Dictionary<string, int> wordCount = new Dictionary<string, int>();
            StringBuilder sbFullText = new StringBuilder();
            foreach (children child in this.children)
            {
                sbFullText.Append(child.SubtreeText);
                sbFullText.Append(" ");
            }
            string[] allWords = GetAllWords(sbFullText.ToString());
            wordCount = new Dictionary<string, int>();
            
            
            Dictionary<string, string> stemParent = new Dictionary<string, string>();
            foreach (string word in allWords)
            {
                try
                {
                    string stemmed = Stemmer.GetStem(word);
                    if (stemParent.ContainsKey(stemmed))
                    {
                        if (stemParent[stemmed].Length < word.Length)
                        {
                            stemParent[stemmed] = word;
                        }
                    }
                    else
                    {
                        stemParent[stemmed] = word;
                    }
                    if (stopWords.Contains(stemmed.ToLower())) continue;
                    if (!wordCount.ContainsKey(stemmed) && !ignoreWords.Contains(stemmed))
                    {
                        wordCount[stemmed] = 1;
                    }
                    else
                    {
                        wordCount[stemmed] += 1;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }
            wordCount = wordCount.OrderByDescending(x => x.Value).Take(N).ToDictionary(kvp=>stemParent[kvp.Key],kvp=>kvp.Value);
            return wordCount;
        } 

        public List<string> GetTopNWords(int N)
        {
            string[] allWords;
            Dictionary<string, int> wordCount = new Dictionary<string, int>();
            List<string> topNWords = new List<string>();
            List<string> topNWordsForComment = new List<string>();
            StringBuilder sbFullText = new StringBuilder();
            foreach (children child in this.children)
            {
                sbFullText.Append(child.SubtreeText);
                sbFullText.Append(" ");
            }
            string tagLess = Util.StripTagsCharArray(sbFullText.ToString());
            wordCount = new Dictionary<string, int>();
            string[] separators = { " ", "." };
            allWords = tagLess.Split(separators, StringSplitOptions.RemoveEmptyEntries);
            foreach (string word in allWords)
            {
                if (stopWords.Contains(word.ToLower())) continue;
                if (!wordCount.ContainsKey(word))
                {
                    wordCount[word] = 1;
                }
                else
                {
                    wordCount[word] += 1;
                }
            }
            topNWordsForComment = wordCount.OrderByDescending(x => x.Value).Select(x => x.Key + "[" + x.Value + "]").Take(N).ToList();
            topNWords.AddRange(topNWordsForComment);
            return topNWords;
        }
    }
}
